namespace LightConvert.Core;

public static class OutputLayout
{
    public static string Folder(string outputRoot, string source, string? importedFolder, bool preserve)
    {
        outputRoot = Path.GetFullPath(outputRoot);
        if (!preserve || importedFolder is null) return outputRoot;
        importedFolder = Path.GetFullPath(importedFolder);
        var relative = Path.GetRelativePath(importedFolder, Path.GetDirectoryName(Path.GetFullPath(source))!);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new InvalidDataException("文件不在导入的文件夹中。");
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(importedFolder));
        if (string.IsNullOrEmpty(name)) name = "Media";
        string result = Path.GetFullPath(Path.Combine(outputRoot, name, relative));
        if (!result.StartsWith(Path.TrimEndingDirectorySeparator(outputRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("输出目录超出保存位置。");
        for (var directory = new DirectoryInfo(result); directory is not null && !directory.FullName.Equals(outputRoot, StringComparison.OrdinalIgnoreCase); directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("输出子文件夹不能是链接。");
        return result;
    }
}
