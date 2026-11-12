using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace DarumaPPAP.PlateauWorldFlow
{
    internal sealed class SdkDiagnostic
    {
        internal string Status { get; }
        internal string Version { get; }
        internal bool ResolvedPinnedSdk { get; }
        internal string Inventory { get; }

        internal SdkDiagnostic(string fingerprint, ProjectSnapshot snapshot, PackageInfo sdk, bool listed, bool supportedEditor, string toolStatus, string toolLocation)
        {
            var evidence = new List<string> { "MANIFEST" };
            var diagnostics = new List<Dictionary<string, object>>();
            if (snapshot.Lock != null)
            {
                evidence.Add("LOCK");
            }
            if (listed)
            {
                evidence.Add("PACKAGE_LIST");
            }

            var entry = snapshot.SdkLock;
            bool officialRequest = snapshot.SdkRequest == DependencyCatalog.Source || snapshot.SdkRequest == DependencyCatalog.GitUrl + "#" + DependencyCatalog.Tag;
            string source = sdk == null ? null : SafeSource(sdk.source);
            bool safeVersion = sdk != null && Regex.IsMatch(sdk.version ?? "", @"\A[A-Za-z0-9][A-Za-z0-9._+-]{0,127}\z", RegexOptions.CultureInvariant);
            Version = safeVersion ? sdk.version : null;
            string commit = sdk?.git?.hash == DependencyCatalog.Commit ? DependencyCatalog.Commit : null;
            ResolvedPinnedSdk = listed && sdk != null && sdk.isDirectDependency && sdk.source == PackageSource.Git && Version == DependencyCatalog.Version && officialRequest && commit != null && Json.Field<string>(entry, "source") == "git" && Json.Field<string>(entry, "version") == snapshot.SdkRequest && Json.Field<string>(entry, "hash") == commit;

            if (!supportedEditor)
            {
                Status = "BLOCKED";
                diagnostics.Add(Diagnostic("UNITY_VERSION_UNSUPPORTED", "ENVIRONMENT", false));
            }
            else if (sdk == null ? snapshot.HasEmbeddedSdk : !ResolvedPinnedSdk)
            {
                Status = "INCOMPATIBLE";
                diagnostics.Add(Diagnostic("EXISTING_SDK_PRESERVED", "PACKAGE_LIST", false));
            }
            else if (!listed || (snapshot.HasSdk && sdk == null))
            {
                Status = "UNKNOWN";
                diagnostics.Add(Diagnostic("PACKAGE_LIST_UNCONFIRMED", "PACKAGE_LIST", true));
            }
            else if (sdk == null)
            {
                Status = "MISSING";
            }
            else if (EditorUtility.scriptCompilationFailed)
            {
                Status = "BLOCKED";
                diagnostics.Add(Diagnostic("SCRIPT_COMPILATION_FAILED", "COMPILE", true));
            }
            else if (EditorApplication.isCompiling || !HasImporterCapability(sdk))
            {
                Status = "UNKNOWN";
                diagnostics.Add(Diagnostic("IMPORT_CAPABILITY_UNCONFIRMED", "COMPILE", true));
            }
            else
            {
                evidence.Add("COMPILE");
                evidence.Add("CAPABILITY");
                Status = "READY";
            }

            if (toolStatus != null)
            {
                diagnostics.Add(Diagnostic(toolStatus, toolLocation, true));
            }

            var package = Json.Object(("packageId", DependencyCatalog.PackageId), ("requiredVersion", DependencyCatalog.Version), ("resolvedVersion", Version), ("source", source), ("sourceUri", officialRequest ? DependencyCatalog.GitUrl : null), ("sourceRef", officialRequest ? snapshot.SdkRequest.Substring(DependencyCatalog.GitUrl.Length + 1) : null), ("resolvedCommit", commit), ("directDependency", sdk == null ? (snapshot.SdkRequest == null ? null : (object)true) : sdk.isDirectDependency), ("status", Status), ("evidence", evidence));
            Inventory = Json.Write(Json.Object(("schemaVersion", 1), ("projectFingerprint", fingerprint), ("unityVersion", Application.unityVersion), ("inspectedAt", Digests.Timestamp(DateTimeOffset.UtcNow)), ("packageList", listed ? "OFFLINE_CACHE_MAY_BE_STALE" : "UNAVAILABLE"), ("packages", new[] { package }), ("tools", Array.Empty<object>()), ("diagnostics", diagnostics)));
        }

        internal static Dictionary<string, object> Diagnostic(string code, string location, bool retryable)
        {
            return Json.Object(("code", code), ("location", location), ("retryable", retryable));
        }

        private static bool HasImporterCapability(PackageInfo sdk)
        {
            const string importerAssembly = "PLATEAU.Runtime";
            const string importerType = "PLATEAU.CityImport.Import.CityImporter";
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(item => item.GetName().Name == importerAssembly);
            var compiledType = assembly?.GetType(importerType, false);
            return compiledType != null && PackageInfo.FindForAssembly(assembly)?.name == sdk.name;
        }

        private static string SafeSource(PackageSource source)
        {
            switch (source)
            {
                case PackageSource.Git: return "GIT";
                case PackageSource.Registry: return "REGISTRY";
                case PackageSource.Embedded: return "EMBEDDED";
                case PackageSource.Local:
                case PackageSource.LocalTarball: return "LOCAL";
                case PackageSource.BuiltIn: return "BUILT_IN";
                default: return null;
            }
        }
    }
}
