namespace AsmodayCat.Agent.Workspace;

public class AccessManager
{
    public bool CanAccess(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return false;

        try
        {
            var testFile = Path.Combine(folderPath, $".acl_probe_{Guid.NewGuid():N}");
            File.WriteAllText(testFile, string.Empty);
            File.Delete(testFile);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
