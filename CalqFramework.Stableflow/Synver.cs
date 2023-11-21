using NuGet.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CalqFramework.Stableflow {

    static class Extension {
        public static string ToUniqueString(this FieldInfo fielfInfo) {
            return $"{fielfInfo.ReflectedType?.FullName} {fielfInfo.Attributes} {fielfInfo}";
        }

        public static string ToUniqueString(this PropertyInfo propertyInfo) {
            return $"{propertyInfo.ReflectedType?.FullName} {propertyInfo.Attributes} {propertyInfo}";
        }

        public static string ToUniqueString(this MethodInfo methodInfo) {
            return $"{methodInfo.ReflectedType?.FullName} {methodInfo.Attributes} {methodInfo}";
        }
    }

    class Synver {

        class Configuration {
            public static string version = "";
        }

        internal enum DifferenceDegree { None = 1, NonBreaking = 2, Breaking = 4 }

        internal record Version(int major, int minor, int patch) {
            public Version(string version) : this(0, 0, 0) {
                var matches = new Regex("([0-9]+)\\.([0-9]+)\\.([0-9]+)").Match(version).Groups.Values.ToArray();
                major = int.Parse(matches[1].Value);
                minor = int.Parse(matches[2].Value);
                patch = int.Parse(matches[3].Value);
            }

            public Version Bump(DifferenceDegree differenceDegree) {
                if (differenceDegree.HasFlag(DifferenceDegree.Breaking)) {
                    return new Version(major, minor + 1, patch);
                } else if (differenceDegree.HasFlag(DifferenceDegree.NonBreaking)) {
                    return new Version(major, minor, patch + 1);
                } else if (differenceDegree.HasFlag(DifferenceDegree.None)) {
                    return this;
                } else {
                    throw new Exception("unknown difference degree");
                }

            }

            public override string ToString() {
                return $"{major}.{minor}.{patch}";
            }
        }

        interface IValuePair<out T> {
            public T baseValue { get; }
            public T modifiedValue { get; }
        }

        class ValuePair<T> : IValuePair<T> {
            public T baseValue { get; }
            public T modifiedValue { get; }

            public ValuePair(T baseValue, T modifiedValue) {
                this.baseValue = baseValue;
                this.modifiedValue = modifiedValue;
            }
        }

        interface IEnumerableDiff<out T> {
            public IEnumerable<T> Missing { get; }
            public IEnumerable<T> Extra { get; }
            public IEnumerable<IValuePair<T>> Different { get; }
        }

        class EnumerableDiff<T> : IEnumerableDiff<T> {
            public IEnumerable<T> Missing { get; }
            public IEnumerable<T> Extra { get; }
            public IEnumerable<IValuePair<T>> Different { get; }

            public EnumerableDiff(IEnumerable<T> missing, IEnumerable<T> extra, IEnumerable<IValuePair<T>> different) {
                Missing = missing;
                Extra = extra;
                Different = different;
            }
        }

        public static IEnumerable<T> GetMembers<T>(Assembly assembly, Func<Type, BindingFlags, T[]> membersGetter, BindingFlags bindingAttr = BindingFlags.Default) where T : MemberInfo {
            var infos = Enumerable.Empty<T>();
            var types = bindingAttr.HasFlag(BindingFlags.NonPublic) ? assembly.GetTypes() : assembly.GetExportedTypes();
            foreach (var type in types) {
                infos = infos.Concat(membersGetter(type, bindingAttr));
            }
            return infos;
        }

        private static IEnumerable<FieldInfo> GetFields(Assembly assembly, BindingFlags bindingAttr = BindingFlags.Default) {
            return GetMembers(assembly, (type, bindingAttr) => type.GetFields(bindingAttr), bindingAttr);
        }

        private static IEnumerable<PropertyInfo> GetProperties(Assembly assembly, BindingFlags bindingAttr = BindingFlags.Default) {
            return GetMembers(assembly, (type, bindingAttr) => type.GetProperties(bindingAttr), bindingAttr);
        }

        private static IEnumerable<MethodInfo> GetMethods(Assembly assembly, BindingFlags bindingAttr = BindingFlags.Default) {
            return GetMembers(assembly, (type, bindingAttr) => type.GetMethods(bindingAttr), bindingAttr);
        }

        // TODO HKT
        private static IEnumerableDiff<T> Compare<T>(IEnumerable<T> baseEnumerable, IEnumerable<T> modifiedEnumerable, Func<T, string> nameGetter, Func<T, T, bool> comparator) {
            var map = new Dictionary<string, T>();
            foreach (var element in modifiedEnumerable) {
                try {
                    map.Add(nameGetter(element), element);
                } catch (System.ArgumentException) {
                    // FIXME
                }
            }

            var missing = new Dictionary<string, T>();
            var different = new List<IValuePair<T>>();

            foreach (T element in baseEnumerable) {
                if (map.ContainsKey(nameGetter(element))) {
                    if (!comparator(map[nameGetter(element)], element)) {
                        different.Add(new ValuePair<T>(element, map[nameGetter(element)]));
                    }
                    map.Remove(nameGetter(element));

                } else {
                    try {
                        missing.Add(nameGetter(element), element);
                    } catch (System.ArgumentException) {
                        // FIXME
                    }
                }
            }

            return new EnumerableDiff<T>(missing.Values, map.Values, different);
        }

        private static IEnumerableDiff<FieldInfo> GetFieldInfoDifferences(Assembly baseAssembly, Assembly modifiedAssembly, BindingFlags bindingAttr) {
            return Compare(GetFields(baseAssembly, bindingAttr), GetFields(modifiedAssembly, bindingAttr),
                    _ => _.ToUniqueString(),
                    (a, b) =>
                        a.ToUniqueString() == b.ToUniqueString()
                    );
        }

        private static IEnumerableDiff<PropertyInfo> GetPropertyInfoDifferences(Assembly baseAssembly, Assembly modifiedAssembly, BindingFlags bindingAttr) {
            return Compare(GetProperties(baseAssembly, bindingAttr), GetProperties(modifiedAssembly, bindingAttr),
                    _ => _.ToUniqueString(),
                    (a, b) =>
                        a.ToUniqueString() == b.ToUniqueString() &&
#pragma warning disable CS8604 // Possible null reference argument.
                        a.GetGetMethod()?.GetMethodBody()?.GetILAsByteArray()?.SequenceEqual(b.GetGetMethod()?.GetMethodBody()?.GetILAsByteArray()) != false && // true if null
                        a.GetSetMethod()?.GetMethodBody()?.GetILAsByteArray()?.SequenceEqual(b.GetSetMethod()?.GetMethodBody()?.GetILAsByteArray()) != false
#pragma warning restore CS8604 // Possible null reference argument.
                    );
        }

        private static IEnumerableDiff<MethodInfo> GetMethodInfoDifferences(Assembly baseAssembly, Assembly modifiedAssembly, BindingFlags bindingAttr) {
            return Compare(GetMethods(baseAssembly, bindingAttr), GetMethods(modifiedAssembly, bindingAttr),
                    _ => _.ToUniqueString(),
                    (a, b) =>
                        a.ToUniqueString() == b.ToUniqueString() &&
#pragma warning disable CS8604 // Possible null reference argument.
                        a.GetMethodBody()?.GetILAsByteArray()?.SequenceEqual(b.GetMethodBody()?.GetILAsByteArray()) != false // true if null
#pragma warning restore CS8604 // Possible null reference argument.
                    );
        }

        private static DifferenceDegree GetPublicMemberInfosDifferenceDegree(IEnumerableDiff<FieldInfo> fieldDifferences, IEnumerableDiff<PropertyInfo> propertyDifferences, IEnumerableDiff<MethodInfo> methodDifferences) {
            var differenceDegree = DifferenceDegree.None;

            foreach (var info in fieldDifferences.Different) {
                if (info.modifiedValue.Attributes == info.baseValue.Attributes) {
                    differenceDegree |= DifferenceDegree.NonBreaking;
                } else {
                    differenceDegree |= DifferenceDegree.Breaking;
                }
            }
            foreach (var info in propertyDifferences.Different) {
                if (info.modifiedValue.Attributes == info.baseValue.Attributes) {
                    differenceDegree |= DifferenceDegree.NonBreaking;
                } else {
                    differenceDegree |= DifferenceDegree.Breaking;
                }
            }
            foreach (var info in methodDifferences.Different) {
                if (info.modifiedValue.Attributes == info.baseValue.Attributes) {
                    differenceDegree |= DifferenceDegree.NonBreaking;
                } else {
                    differenceDegree |= DifferenceDegree.Breaking;
                }
            }

            if (fieldDifferences.Missing.Count() != 0 || propertyDifferences.Missing.Count() != 0 || methodDifferences.Missing.Count() != 0) {
                differenceDegree |= DifferenceDegree.Breaking;
            }

            if (fieldDifferences.Extra.Count() != 0 || propertyDifferences.Extra.Count() != 0 || methodDifferences.Extra.Count() != 0) {
                differenceDegree |= DifferenceDegree.NonBreaking;
            }

            return differenceDegree;
        }

        private static DifferenceDegree GetPrivateMemberInfosDifferenceDegree<T>(params IEnumerableDiff<T>[] differences) where T : MemberInfo {
            var differenceDegree = DifferenceDegree.None;

            foreach (var difference in differences) {
                if (difference.Different.Count() != 0 || difference.Missing.Count() != 0 || difference.Extra.Count() != 0) {
                    differenceDegree |= DifferenceDegree.NonBreaking;
                }
            }

            return differenceDegree;
        }

        private static void PrintDifferences(IEnumerableDiff<FieldInfo> fieldDifferences, IEnumerableDiff<PropertyInfo> propertyDifferences, IEnumerableDiff<MethodInfo> methodDifferences) {
            var output = new StringBuilder();

            output.AppendLine("[changed]");
            foreach (var info in fieldDifferences.Different) {
                output.AppendLine(info.modifiedValue.ToUniqueString());
            }
            foreach (var info in propertyDifferences.Different) {
                output.AppendLine(info.modifiedValue.ToUniqueString());
            }
            foreach (var info in methodDifferences.Different) {
                output.AppendLine(info.modifiedValue.ToUniqueString());
            }
            output.AppendLine("");
            output.AppendLine("[deleted]");
            foreach (var info in fieldDifferences.Missing) {
                output.AppendLine(info.ToUniqueString());
            }
            foreach (var info in propertyDifferences.Missing) {
                output.AppendLine(info.ToUniqueString());
            }
            foreach (var info in methodDifferences.Missing) {
                output.AppendLine(info.ToUniqueString());
            }
            output.AppendLine("");
            output.AppendLine("[added]");
            foreach (var info in fieldDifferences.Extra) {
                output.AppendLine(info.ToUniqueString());
            }
            foreach (var info in propertyDifferences.Extra) {
                output.AppendLine(info.ToUniqueString());
            }
            foreach (var info in methodDifferences.Extra) {
                output.AppendLine(info.ToUniqueString());
            }

            Console.WriteLine(output);
        }

        private static Version CompareAssemblies(Version version, Assembly baseAssembly, Assembly modifiedAssembly) {
            var bindingAttr = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;

            var fieldInfosDiff = GetFieldInfoDifferences(baseAssembly, modifiedAssembly, bindingAttr);
            var propertyInfosDiff = GetPropertyInfoDifferences(baseAssembly, modifiedAssembly, bindingAttr);
            var methodInfosDiff = GetMethodInfoDifferences(baseAssembly, modifiedAssembly, bindingAttr);
            var differenceDegree = GetPublicMemberInfosDifferenceDegree(fieldInfosDiff, propertyInfosDiff, methodInfosDiff);

            bindingAttr = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
            differenceDegree |= GetPrivateMemberInfosDifferenceDegree<MemberInfo>(
                GetFieldInfoDifferences(baseAssembly, modifiedAssembly, bindingAttr),
                GetPropertyInfoDifferences(baseAssembly, modifiedAssembly, bindingAttr),
                GetMethodInfoDifferences(baseAssembly, modifiedAssembly, bindingAttr)
            );

            if (differenceDegree == DifferenceDegree.None) {
                differenceDegree |= File.ReadAllBytes(baseAssembly.Location).SequenceEqual(File.ReadAllBytes(modifiedAssembly.Location)) ? DifferenceDegree.None : DifferenceDegree.NonBreaking;
            }

            var bumpedVersion = version.Bump(differenceDegree);
            Console.WriteLine(bumpedVersion);
            Console.WriteLine("");
            PrintDifferences(fieldInfosDiff, propertyInfosDiff, methodInfosDiff);
            return bumpedVersion;
        }

        internal static Version CompareAssemblies(string baseAssemblyPath, string modifiedAssemblyPath) {
            var runtimeAssemblies = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");
            var assemblyPaths = new List<string>(runtimeAssemblies);

            var baseAssemblies = Directory.GetFiles(Path.GetDirectoryName(baseAssemblyPath)!, "*.dll", new EnumerationOptions { RecurseSubdirectories = true });
            assemblyPaths.AddRange(baseAssemblies);

            var modofiedAssemblies = Directory.GetFiles(Path.GetDirectoryName(modifiedAssemblyPath)!, "*.dll", new EnumerationOptions { RecurseSubdirectories = true });
            assemblyPaths.AddRange(modofiedAssemblies);

            // fails to find globalPackagesFolder on GitHub Actions
            // System.IO.DirectoryNotFoundException: Could not find a part of the path '/home/runner/.nuget/packages'.
            var globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(Settings.LoadDefaultSettings(null));
            if (Directory.Exists(globalPackagesFolder)) {
                var globalPackagesLibFolders = Directory.GetDirectories(globalPackagesFolder, "lib", new EnumerationOptions { RecurseSubdirectories = true });
                foreach (var libFolder in globalPackagesLibFolders) {
                    var assemblies = Directory.GetFiles(libFolder, "*.dll", new EnumerationOptions { RecurseSubdirectories = true });
                    foreach (var assembly in assemblies) {
                        if (assembly.StartsWith(Path.Combine(globalPackagesFolder, "microsoft.netcore.app.runtime"))) { // TODO validate if _correct_
                            continue; // otherwise mscorlib.dll is loaded twice and fails
                        }
                        assemblyPaths.Add(assembly);
                    }
                }
            }

            Assembly baseAssembly = new MetadataLoadContext(new PathAssemblyResolver(assemblyPaths)).LoadFromAssemblyPath(baseAssemblyPath);
            Assembly modifiedAssembly = new MetadataLoadContext(new PathAssemblyResolver(assemblyPaths)).LoadFromAssemblyPath(modifiedAssemblyPath);

            var assemblyVersion = baseAssembly.GetName().Version!;
            var version = Configuration.version != "" ? new Version(Configuration.version) : new Version(assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);

            return CompareAssemblies(version, baseAssembly, modifiedAssembly);
        }
    }
}
