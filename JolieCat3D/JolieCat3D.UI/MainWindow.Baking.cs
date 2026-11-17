using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using JolieCat3D.Core.Camera;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Core.Sculpting;
using JolieCat3D.Core.Skinning;
using JolieCat3D.Engine.Editing;
using JolieCat3D.Engine.Gizmos;
using JolieCat3D.Engine.Lighting;
using JolieCat3D.Engine.Rendering;
using JolieCat3D.Engine.Viewport;
using JolieCat3D.Service;
using JolieCat3D.Service.Animation;
using JolieCat3D.Service.Commands;
using JolieCat3D.Service.Export;
using JolieCat3D.Service.Interop;
using JolieCat3D.Service.Project;
using JolieCat3D.UI.ViewModels;
using Microsoft.Win32;

namespace JolieCat3D.UI
{
    /// <summary>Texture baking and the Graph Editor.</summary>
    public partial class MainWindow
    {
        // ================= Texture baking =================

        private bool _isBaking;

        private static void SavePng(TextureBuffer buffer, string path)
        {
            var pixels = new byte[buffer.Width * buffer.Height * 4];
            for (var i = 0; i < buffer.Pixels.Count; i++)
            {
                var c = buffer.Pixels[i];
                pixels[i * 4] = (byte)Math.Clamp((int)MathF.Round(c.B * 255), 0, 255);
                pixels[i * 4 + 1] = (byte)Math.Clamp((int)MathF.Round(c.G * 255), 0, 255);
                pixels[i * 4 + 2] = (byte)Math.Clamp((int)MathF.Round(c.R * 255), 0, 255);
                pixels[i * 4 + 3] = 255;
            }
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(buffer.Width, buffer.Height, 96, 96, PixelFormats.Bgra32, null, pixels, buffer.Width * 4);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private string? AskBakePath(Node node, string suffix, string filter = "PNG image (*.png)|*.png", string extension = ".png")
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                Title = "Save Baked Texture",
                FileName = $"{node.Name}_{suffix}{extension}",
                InitialDirectory = _currentFilePath is { } project ? Path.GetDirectoryName(project) : null,
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        private static bool HasUsableUvs(Mesh mesh)
        {
            if (mesh.Vertices.Count == 0) return false;
            var min = mesh.Vertices.Aggregate(new System.Numerics.Vector2(float.MaxValue), (m, v) => System.Numerics.Vector2.Min(m, v.UV));
            var max = mesh.Vertices.Aggregate(new System.Numerics.Vector2(float.MinValue), (m, v) => System.Numerics.Vector2.Max(m, v.UV));
            return (max - min).X > 1e-4f && (max - min).Y > 1e-4f;
        }

        /// <summary>Runs a bake in the background, saves the PNG and assigns it to the
        /// object's material (one undo step).</summary>
        private Task RunBake(string name, string suffix, Func<Node, IProgress<float>, TextureBuffer?> bake, Action<Material, string>? assign) =>
            RunBake(name, suffix, "PNG image (*.png)|*.png", ".png", (node, progress, path) =>
            {
                if (bake(node, progress) is not { } result) return false;
                SavePng(result, path);
                return true;
            }, assign);

        /// <summary>Bakes and saves in the background; <paramref name="bakeAndSave"/> gets
        /// the chosen file path (its extension picks the format).</summary>
        private async Task RunBake(string name, string suffix, string filter, string extension,
            Func<Node, IProgress<float>, string, bool> bakeAndSave, Action<Material, string>? assign)
        {
            if (_isBaking) { Viewport.ShowMessage("A bake is already running."); return; }
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: { } mesh } node)
            {
                Viewport.ShowMessage("Select a mesh object to bake.");
                return;
            }
            if (!HasUsableUvs(mesh))
            {
                Viewport.ShowMessage("This mesh has no UV layout - use Unwrap (Edit Mode) first.");
                return;
            }
            if (AskBakePath(node, suffix, filter, extension) is not { } path) return;

            _isBaking = true;
            try
            {
                var progress = new Progress<float>(p => Viewport.ShowMessage($"{name}: {p:P0}"));
                Viewport.ShowMessage($"{name}: starting...");
                if (!await Task.Run(() => bakeAndSave(node, progress, path))) return;
                if (assign is null)
                {
                    Viewport.ShowMessage($"{name} saved to {Path.GetFileName(path)}.");
                    return;
                }
                RunObjectOperator(name, n =>
                {
                    if (n.Mesh is not { } m) return false;
                    m.Material ??= new Material(n.Name + "_Material");
                    assign(m.Material, path);
                    return true;
                });
                Viewport.ShowMessage($"{name} saved to {Path.GetFileName(path)} and assigned to the material.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{name} failed:\n{ex.Message}", "JolieCat3D", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isBaking = false;
            }
        }

        /// <summary>Lightmap: direct + indirect lighting path-traced into the object's UVs,
        /// saved for the game engine as Radiance .hdr (linear, unclamped) or PNG (sRGB,
        /// clipped at white); the material is left unchanged.</summary>
        private async void BakeLightmapMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: not null }) { Viewport.ShowMessage("Select a mesh object to bake."); return; }
            var dialog = new BakeSettingsWindow("Bake Lightmap", showSamples: true, "Light bounces", _renderOptions.Bounces) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not { } settings) return;
            var bounces = Math.Clamp((int)MathF.Round(settings.Distance), 0, 16);
            // The tracer reads the scene on this thread; only the texel work runs in the background.
            var tracer = new PathTracer(_currentScene, Viewport.Camera.Clone(), 1, 1,
                new PathTracerSettings { MaxBounces = bounces, SkyStrength = _renderOptions.SkyStrength, Exposure = _renderOptions.Exposure });
            var node = _sceneViewModel.SelectedNode!.UnderlyingNode;
            var drawn = Core.Modifiers.ModifierStack.Evaluate(node.Mesh!, node.Modifiers, node);
            var world = node.GetWorldTransform();
            await RunBake("Bake Lightmap", "Lightmap", "Radiance HDR, linear (*.hdr)|*.hdr|PNG image, sRGB (*.png)|*.png", ".hdr", (_, progress, path) =>
            {
                if (string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                    SavePng(tracer.BakeLightmap(drawn, world, settings.Resolution, settings.Samples, progress), path);
                else
                    tracer.BakeLightmapHdr(drawn, world, settings.Resolution, settings.Samples, progress).SaveHdr(path);
                return true;
            }, assign: null);
        }

        private async void BakeAoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            // Occluders: every mesh in the scene as it is drawn (modifiers applied), in world space.
            var occluders = new List<(System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3)>();
            foreach (var n in _currentScene.Traverse())
            {
                if (n.Mesh is not { } m) continue;
                var evaluated = Core.Modifiers.ModifierStack.Evaluate(m, n.Modifiers, n);
                var world = n.GetWorldTransform();
                foreach (var f in evaluated.GetRenderFaces())
                    occluders.Add((
                        System.Numerics.Vector3.Transform(evaluated.Vertices[f.A].Position, world),
                        System.Numerics.Vector3.Transform(evaluated.Vertices[f.B].Position, world),
                        System.Numerics.Vector3.Transform(evaluated.Vertices[f.C].Position, world)));
            }
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: not null } target) { Viewport.ShowMessage("Select a mesh object to bake."); return; }
            var (bmin, bmax) = target.GetWorldBounds();
            var dialog = new BakeSettingsWindow("Bake Ambient Occlusion", showSamples: true, "AO distance", MathF.Max(0.05f, (bmax - bmin).Length() * 0.5f)) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not { } settings) return;
            await RunBake("Bake Ambient Occlusion", "AO", (node, progress) =>
            {
                // Bake on the mesh as drawn (modifiers applied): its UVs are what the texture maps onto.
                var drawn = Core.Modifiers.ModifierStack.Evaluate(node.Mesh!, node.Modifiers, node);
                return TextureBaker.BakeAmbientOcclusion(drawn, node.GetWorldTransform(), new TriangleBvh(occluders), settings.Resolution, settings.Samples, settings.Distance, progress);
            }, (material, path) => material.OcclusionTexturePath = path);
        }

        /// <summary>Curvature mask (convex edges light, cavities dark) over the object's UVs - for
        /// edge wear and dirt in texture painting. Saved as a PNG only.</summary>
        private async void BakeCurvatureMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: not null } curvTarget) { Viewport.ShowMessage("Select a mesh object to bake."); return; }
            var (cmin, cmax) = curvTarget.Mesh!.GetBounds();
            var dialog = new BakeSettingsWindow("Bake Curvature", showSamples: false, "Edge width", MathF.Max(0.001f, (cmax - cmin).Length() * 0.02f)) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not { } settings) return;
            await RunBake("Bake Curvature", "Curvature", (node, progress) =>
            {
                var drawn = Core.Modifiers.ModifierStack.Evaluate(node.Mesh!, node.Modifiers, node);
                return TextureBakerExtras.BakeCurvature(drawn, settings.Resolution, settings.Distance, progress);
            }, assign: null);
        }

        /// <summary>ID map: a flat colour per material slot (or per separate part) - selection
        /// masks for texture painting tools. Saved as a PNG only.</summary>
        private async void BakeIdMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            var dialog = new BakeSettingsWindow("Bake ID Map", showSamples: false, "(unused)", 1f) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not { } settings) return;
            await RunBake("Bake ID Map", "ID", (node, progress) =>
            {
                var drawn = Core.Modifiers.ModifierStack.Evaluate(node.Mesh!, node.Modifiers, node);
                return TextureBakerExtras.BakeId(drawn, settings.Resolution, progress);
            }, assign: null);
        }

        private async void BakeNormalMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is { } selected && !selected.Modifiers.Any(m => m.IsEnabled))
            {
                Viewport.ShowMessage("Normal bake uses the object's modifiers (e.g. Subdivision Surface) as the detailed version - add one first.");
                return;
            }
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: { } baseMesh }) { Viewport.ShowMessage("Select a mesh object to bake."); return; }
            var (lmin, lmax) = baseMesh.GetBounds();
            var dialog = new BakeSettingsWindow("Bake Normal Map", showSamples: false, "Cage distance", MathF.Max(0.01f, (lmax - lmin).Length() * 0.05f)) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not { } settings) return;
            await RunBake("Bake Normal Map", "Normal", (node, progress) =>
            {
                var low = node.Mesh!;
                var high = Core.Modifiers.ModifierStack.Evaluate(low, node.Modifiers, node);
                return TextureBaker.BakeNormalMap(low, high, settings.Resolution, settings.Distance, progress);
            }, (material, path) => material.NormalTexturePath = path);
        }

        // ================= Graph Editor & keyframe hotkeys =================

        private GraphEditorWindow? _graphEditor;

        private void OpenGraphEditor()
        {
            if (_graphEditor is not null) { _graphEditor.Activate(); return; }
            _graphEditor = new GraphEditorWindow(
                () => _timeline,
                () => _sceneViewModel.SelectedNode?.UnderlyingNode,
                CommitGraphEdit,
                PreviewAnimation,
                SetAnimationTime)
            { Owner = this };
            _graphEditor.Closed += (_, _) => _graphEditor = null;
            _graphEditor.Show();
        }

        private void GraphEditorMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized) OpenGraphEditor(); }

        // ================= NLA Editor =================

        private NlaEditorWindow? _nlaEditor;

        private void OpenNlaEditor()
        {
            if (_nlaEditor is not null) { _nlaEditor.Activate(); return; }
            _nlaEditor = new NlaEditorWindow(
                () => _timeline,
                command => _commandHistory.Record(command),
                SetAnimationTime,
                OnActionsChanged)
            { Owner = this };
            _nlaEditor.Closed += (_, _) => _nlaEditor = null;
            _nlaEditor.Show();
        }

        private void NlaEditorMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized) OpenNlaEditor(); }

        // ================= Shader Editor =================

        private ShaderEditorWindow? _shaderEditor;

        private void OpenShaderEditor()
        {
            if (_shaderEditor is not null) { _shaderEditor.Activate(); return; }
            _shaderEditor = new ShaderEditorWindow(ShaderEditorTarget, CommitMaterialEdit, OnNodeStateRestored) { Owner = this };
            _shaderEditor.Closed += (_, _) => _shaderEditor = null;
            _shaderEditor.Show();
        }

        /// <summary>The active object's material (a mesh without one gets a default material
        /// first, so there is something to put nodes on).</summary>
        private (Node Node, Material Material)? ShaderEditorTarget()
        {
            if (_sceneViewModel.SelectedNode?.UnderlyingNode is not { Mesh: { } mesh } node) return null;
            mesh.Material ??= Material.CreateDefault();
            return (node, mesh.Material);
        }

        /// <summary>Runs a material change on <paramref name="node"/> as one undo step.</summary>
        private void CommitMaterialEdit(Node node, string description, Action change)
        {
            var before = NodeState.Capture(node);
            change();
            var after = NodeState.Capture(node, before);
            if (!after.ContentEquals(before))
                _commandHistory.Record(new NodeStateCommand(node, before, after, description, () => { OnNodeStateRestored(node); _shaderEditor?.Rebuild(); }));
            OnNodeStateRestored(node);
            MarkChanged();
        }

        private void ShaderEditorMenuItem_Click(object sender, RoutedEventArgs e) { if (_isInitialized) OpenShaderEditor(); }
    }
}
