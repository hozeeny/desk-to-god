using System;
namespace NetBunny
{
    internal enum DesktopEnvironmentMode { Virtual, Observe, Maintain }
    internal enum DesktopMaintenanceScope { FileCleanup, MemoryReclaim, ProcessExit }

    // Profile isolation is a storage choice. This immutable entry-point ceiling
    // is separate; the scope grants remain in the existing consent store.
    internal sealed class DesktopEnvironmentPolicy
    {
        readonly bool virtualOnly;
        internal DesktopEnvironmentPolicy(bool virtualOnly) { this.virtualOnly=virtualOnly; }
        internal bool MayObserve { get { return !virtualOnly; } }
        internal DesktopEnvironmentMode ReadMode(bool cleanupConsent,bool memoryConsent)
        { return virtualOnly?DesktopEnvironmentMode.Virtual:cleanupConsent||memoryConsent?DesktopEnvironmentMode.Maintain:DesktopEnvironmentMode.Observe; }
        internal bool MayMaintain(DesktopMaintenanceScope scope,bool scopeConsent,bool automatic,bool automaticConsent)
        {
            if(virtualOnly||!Enum.IsDefined(typeof(DesktopMaintenanceScope),scope)||!scopeConsent)return false;
            if(scope==DesktopMaintenanceScope.ProcessExit)return !automatic;
            return !automatic||automaticConsent;
        }
    }
}
