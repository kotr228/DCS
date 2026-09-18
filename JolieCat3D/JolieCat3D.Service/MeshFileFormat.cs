namespace JolieCat3D.Service
{
    /// <summary>The 3D file formats <see cref="MeshFileService"/> reads/writes - see its
    /// own remarks, and <c>JolieCat3D.Service.Obj</c>/<c>JolieCat3D.Service.Stl</c>, for
    /// why these two and not a third for <c>.fbx</c>. <see cref="Gltf"/> covers both
    /// <c>.glb</c> (binary) and <c>.gltf</c> (separate-files JSON) - see
    /// <see cref="Import.GltfImporter"/>/<see cref="Export.GltfExporter"/>.</summary>
    public enum MeshFileFormat
    {
        Obj,
        Stl,
        Gltf,
    }
}
