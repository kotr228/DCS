using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Wavefront;
using JolieCat3D.Service.Stl;
using JolieCat3D.Service.Import;
using JolieCat3D.Service.Export;

namespace JolieCat3D.Service
{
    /// <summary>
    /// The single entry point <c>JolieCat3D.UI</c>'s File menu actually calls for every
    /// Open/Save/Import/Export command - dispatches to <see cref="ObjImporter"/>/
    /// <see cref="ObjExporter"/>, <see cref="StlImporter"/>/<see cref="StlExporter"/>, or
    /// <see cref="GltfImporter"/>/<see cref="GltfExporter"/> by file extension, so the UI
    /// layer never needs to know which concrete importer/exporter handles which format,
    /// the same "one facade, not one class per caller to know about" shape as
    /// <c>JolieCat3D.Engine.Rendering.Scene3DRenderer</c>. <see cref="ExportScene"/>'s own
    /// glTF branch is a plain STATIC export (no <c>AnimationTimeline</c> - this method
    /// never received one to begin with); the File menu's own dedicated "Export GLB/
    /// glTF..." command (<c>MainWindow.ExportGltfMenuItem_Click</c>) calls
    /// <see cref="GltfExporter.Export"/> directly with the live timeline attached instead,
    /// exactly when a caller actually wants animation carried along.
    ///
    /// <c>.fbx</c> is not supported, by deliberate choice, not an oversight: real FBX is
    /// either Autodesk's proprietary binary format or a large, intricate ASCII
    /// object-graph-with-connections format - implementing either correctly from
    /// scratch, with no FBX-consuming tool available in this environment to validate a
    /// round trip against, risks shipping a writer that looks plausible in code review
    /// but produces files real FBX importers silently reject or misinterpret. OBJ + STL
    /// (the task's own named alternative to FBX) are both simple, fully-specified text
    /// formats this project can implement completely and correctly; glTF/GLB is a third,
    /// added later once a real PBR/animation-capable format was needed (see
    /// <see cref="GltfExporter"/>'s own remarks) - just as fully, correctly implemented,
    /// just via a real third-party library (<c>SharpGLTF</c>) rather than a from-scratch
    /// parser, since unlike FBX that library IS available in this environment to
    /// validate a round trip against.
    /// </summary>
    public static class MeshFileService
    {
        public const string ObjFilter = "Wavefront OBJ (*.obj)|*.obj";
        public const string StlFilter = "Stereolithography (*.stl)|*.stl";
        public const string GltfFilter = "glTF/GLB (*.glb;*.gltf)|*.glb;*.gltf";
        public const string AnyMeshFilter = "3D Models (*.obj;*.stl;*.glb;*.gltf)|*.obj;*.stl;*.glb;*.gltf";

        public static MeshFileFormat? DetectFormat(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".obj" => MeshFileFormat.Obj,
            ".stl" => MeshFileFormat.Stl,
            ".glb" or ".gltf" => MeshFileFormat.Gltf,
            _ => null,
        };

        /// <summary>Loads <paramref name="filePath"/> as a whole <see cref="Scene3D"/> -
        /// an OBJ file's own multi-object structure carries straight across (see
        /// <see cref="ObjImporter"/>); an STL file (always a single triangle soup, per
        /// the format itself) becomes one root node wrapping it; a glTF/GLB file's own
        /// real node hierarchy carries straight across too (see
        /// <see cref="GltfImporter"/>).</summary>
        public static Scene3D ImportScene(string filePath)
        {
            switch (DetectFormat(filePath))
            {
                case MeshFileFormat.Obj:
                    return ObjImporter.Import(filePath);

                case MeshFileFormat.Stl:
                    var mesh = StlImporter.Import(filePath);
                    var scene = new Scene3D(mesh.Name);
                    scene.AddRootNode(new Node(mesh.Name) { Mesh = mesh });
                    return scene;

                case MeshFileFormat.Gltf:
                    return GltfImporter.Import(filePath);

                default:
                    throw new NotSupportedException($"Unsupported 3D file format: '{Path.GetExtension(filePath)}'.");
            }
        }

        /// <summary>Writes the whole <paramref name="scene"/> to <paramref name="filePath"/>,
        /// in whichever format its extension names.</summary>
        public static void ExportScene(Scene3D scene, string filePath)
        {
            switch (DetectFormat(filePath))
            {
                case MeshFileFormat.Obj:
                    ObjExporter.Export(scene, filePath);
                    break;

                case MeshFileFormat.Stl:
                    StlExporter.ExportScene(scene, filePath);
                    break;

                case MeshFileFormat.Gltf:
                    GltfExporter.Export(scene, timeline: null, filePath);
                    break;

                default:
                    throw new NotSupportedException($"Unsupported 3D file format: '{Path.GetExtension(filePath)}'.");
            }
        }
    }
}
