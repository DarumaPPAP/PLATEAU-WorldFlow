using System;

namespace DarumaPPAP.PlateauWorldFlow
{
    internal readonly struct InstallContext
    {
        internal string Project { get; }
        internal string Manifest { get; }
        internal string Lock { get; }
        internal DateTimeOffset Now { get; }
        internal bool CanAdd { get; }

        internal InstallContext(string project, string manifest, string packageLock, DateTimeOffset now, bool canAdd)
        {
            Project = project;
            Manifest = manifest;
            Lock = packageLock;
            Now = now;
            CanAdd = canAdd;
        }
    }
}
