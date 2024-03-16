using System.Collections.Generic;
using System.Runtime;

namespace GameHook.Domain;

public static class MapperFilesystemProviderExtensions
{
    public static string[] GetFilesByExtensions(this string unused, string path, string[] searchPatterns, SearchOption searchOption)
    {
        IEnumerable<string>? res = null;

        foreach (string searchPattern in searchPatterns)
        {
            if (res == null)
            {
                res = Directory.EnumerateFiles(path, searchPattern, searchOption);
            }
            else
            {
                res = res.Union(Directory.EnumerateFiles(path, searchPattern, searchOption));
            }
        }
        if (res != null)
        {
            return res.ToArray();
        }
        else
        {
            return Array.Empty<string>();
        }
    }

    public static FileInfo[] GetFilesByExtensions(this DirectoryInfo dirInfo, string[] searchPatterns, SearchOption searchOption)
    {
        IEnumerable<FileInfo>? res = null;

        foreach(string searchPattern in searchPatterns)
        {
            if(res == null)
            {
                res = dirInfo.EnumerateFiles(searchPattern, searchOption);
            }
            else
            {
                res = res.Union(dirInfo.EnumerateFiles(searchPattern, searchOption));
            }
        }
        if(res != null)
        {
            return res.ToArray();
        }
        else
        {
            return Array.Empty<FileInfo>();
        }
    }

    public static IEnumerable<FileInfo> EnumerateFilesByExtensions(this DirectoryInfo dirInfo, string[] searchPatterns, SearchOption searchOption)
    {
        IEnumerable<FileInfo>? res = null;

        foreach (string searchPattern in searchPatterns)
        {
            if (res == null)
            {
                res = dirInfo.EnumerateFiles(searchPattern, searchOption);
            }
            else
            {
                res = res.Union(dirInfo.EnumerateFiles(searchPattern, searchOption));
            }
        }
        if (res != null)
        {
            return res;
        }
        else
        {
            return Array.Empty<FileInfo>();
        }
    }
}