namespace GitVersion
{
    using Helpers;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Xml.Linq;

    class CsprojFileUpdate : IDisposable
    {
        List<Action> restoreBackupTasks = new List<Action>();
        List<Action> cleanupBackupTasks = new List<Action>();

        public CsprojFileUpdate(Arguments args, string workingDirectory, VersionVariables variables, IFileSystem fileSystem)
        {
            if (!args.UpdateCsproj) return;

            if (args.Output != OutputType.Json)
                Logger.WriteInfo("Updating csproj files");

            var csprojFiles = fileSystem.DirectoryGetFiles(workingDirectory, "*.csproj", SearchOption.AllDirectories).Select(x => new FileInfo(x)).ToList();
            Logger.WriteInfo($"Found {csprojFiles.Count} files");

            foreach (var csprojFile in csprojFiles)
            {
                var backupAssemblyInfo = csprojFile.FullName + ".bak";
                var localAssemblyInfo = csprojFile.FullName;
                fileSystem.Copy(csprojFile.FullName, backupAssemblyInfo, true);
                restoreBackupTasks.Add(() =>
                {
                    if (fileSystem.Exists(localAssemblyInfo))
                        fileSystem.Delete(localAssemblyInfo);
                    fileSystem.Move(backupAssemblyInfo, localAssemblyInfo);
                });
                cleanupBackupTasks.Add(() => fileSystem.Delete(backupAssemblyInfo));

                var originalFileContents = fileSystem.ReadAllText(csprojFile.FullName);
                var fileContents = originalFileContents;

                var xmlDoc = XDocument.Parse(fileContents);

                XElement versionPrefixElement = xmlDoc.Descendants("VersionPrefix").FirstOrDefault();
                XElement versionSuffixElement = xmlDoc.Descendants("VersionSuffix").FirstOrDefault();

                if (versionPrefixElement == null || versionSuffixElement == null)
                {
                    XElement propertyGroupElement = null;
                    var assemblyNameElement = xmlDoc.Descendants("AssemblyName").FirstOrDefault();
                    if (assemblyNameElement != null)
                    {
                        if (assemblyNameElement.Parent?.Name.LocalName == "PropertyGroup")
                            propertyGroupElement = assemblyNameElement.Parent;
                    }

                    if (propertyGroupElement != null)
                    {
                        if (versionPrefixElement == null)
                            propertyGroupElement.Add(versionPrefixElement = new XElement("VersionPrefix"));

                        if (versionSuffixElement == null)
                            propertyGroupElement.Add(versionSuffixElement = new XElement("VersionSuffix"));
                    }
                }

                if (versionPrefixElement != null)
                    versionPrefixElement.Value = variables.MajorMinorPatch;
                if (versionSuffixElement != null)
                    versionSuffixElement.Value = variables.NuGetPreReleaseTagV2;

                fileContents = xmlDoc.ToString();

                if (originalFileContents != fileContents)
                {
                    fileSystem.WriteAllText(csprojFile.FullName, fileContents);
                }
            }
        }

        public void Dispose()
        {
            foreach (var restoreBackup in restoreBackupTasks)
            {
                restoreBackup();
            }

            cleanupBackupTasks.Clear();
            restoreBackupTasks.Clear();
        }

        public void DoNotRestoreAssemblyInfo()
        {
            foreach (var cleanupBackupTask in cleanupBackupTasks)
            {
                cleanupBackupTask();
            }
            cleanupBackupTasks.Clear();
            restoreBackupTasks.Clear();
        }
    }
}