﻿using NuGet.Packaging.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NuGet.PackageManagement
{
    public static class UninstallResolver
    {
        public static IDictionary<PackageIdentity, HashSet<PackageIdentity>> GetPackageDependents(IEnumerable<PackageDependencyInfo> dependencyInfoEnumerable,
            IEnumerable<PackageIdentity> installedPackages, out IDictionary<PackageIdentity, HashSet<PackageIdentity>> dependenciesDict)
        {
            Dictionary<PackageIdentity, HashSet<PackageIdentity>> dependentsDict = new Dictionary<PackageIdentity, HashSet<PackageIdentity>>(PackageIdentity.Comparer);
            dependenciesDict = new Dictionary<PackageIdentity, HashSet<PackageIdentity>>(PackageIdentity.Comparer);
            foreach (var dependencyInfo in dependencyInfoEnumerable)
            {
                var packageIdentity = new PackageIdentity(dependencyInfo.Id, dependencyInfo.Version);
                foreach(var dependency in dependencyInfo.Dependencies)
                {
                    var dependencyPackageIdentity = installedPackages.Where(i => dependency.Id.Equals(i.Id, StringComparison.OrdinalIgnoreCase)
                        && dependency.VersionRange.Satisfies(i.Version)).FirstOrDefault();
                    if(dependencyPackageIdentity != null)
                    {
                        // Update the package dependents dictionary
                        HashSet<PackageIdentity> dependents;
                        if(!dependentsDict.TryGetValue(dependencyPackageIdentity, out dependents))
                        {
                            dependentsDict[dependencyPackageIdentity] = dependents = new HashSet<PackageIdentity>(PackageIdentity.Comparer);
                        }
                        dependents.Add(packageIdentity);

                        // Update the package dependencies dictionary
                        HashSet<PackageIdentity> dependencies;
                        if(!dependenciesDict.TryGetValue(packageIdentity, out dependencies))
                        {
                            dependenciesDict[packageIdentity] = dependencies = new HashSet<PackageIdentity>(PackageIdentity.Comparer);
                        }
                        dependencies.Add(dependencyPackageIdentity);
                    }
                }
            }

            return dependentsDict;
        }

        public static ICollection<PackageIdentity> GetPackagesToBeUninstalled(PackageIdentity packageIdentity, IEnumerable<PackageDependencyInfo> dependencyInfoEnumerable,
            IEnumerable<PackageIdentity> installedPackages, UninstallationContext uninstallationContext)
        {
            IDictionary<PackageIdentity, HashSet<PackageIdentity>> dependenciesDict;
            var dependentsDict = GetPackageDependents(dependencyInfoEnumerable, installedPackages, out dependenciesDict);
            HashSet<PackageIdentity> packagesMarkedForUninstall =
                MarkPackagesToBeUninstalled(packageIdentity, dependenciesDict, dependentsDict, uninstallationContext);

            CheckIfPackageCanBeUninstalled(packageIdentity, dependenciesDict, dependentsDict, uninstallationContext, packagesMarkedForUninstall);
            return packagesMarkedForUninstall;
        }

        private static void CheckIfPackageCanBeUninstalled(PackageIdentity packageIdentity,
            IDictionary<PackageIdentity, HashSet<PackageIdentity>> dependenciesDict,
            IDictionary<PackageIdentity, HashSet<PackageIdentity>> dependentsDict,
            UninstallationContext uninstallationContext,
            HashSet<PackageIdentity> packagesMarkedForUninstall)
        {
            HashSet<PackageIdentity> dependents;
            if (dependentsDict.TryGetValue(packageIdentity, out dependents) && dependents != null)
            {
                if (!uninstallationContext.ForceRemove)
                {
                    var unMarkedDependents = dependents.Where(d => !packagesMarkedForUninstall.Contains(d)).ToList();
                    if (unMarkedDependents.Count > 0)
                    {
                        throw CreatePackageHasDependentsException(packageIdentity, unMarkedDependents);
                    }
                }
            }

            HashSet<PackageIdentity> dependencies;
            if (uninstallationContext.RemoveDependencies && dependenciesDict.TryGetValue(packageIdentity, out dependencies) && dependencies != null)
            {
                foreach(var dependency in dependencies)
                {
                    CheckIfPackageCanBeUninstalled(dependency,
                        dependenciesDict,
                        dependentsDict,
                        uninstallationContext,
                        packagesMarkedForUninstall);
                }
            }
        }

        private static HashSet<PackageIdentity> MarkPackagesToBeUninstalled(PackageIdentity packageIdentity,
            IDictionary<PackageIdentity, HashSet<PackageIdentity>> dependenciesDict,
            IDictionary<PackageIdentity, HashSet<PackageIdentity>> dependentsDict,
            UninstallationContext uninstallationContext)
        {
            Queue<PackageIdentity> breathFirstSearchQueue = new Queue<PackageIdentity>();
            List<PackageIdentity> markedPackages = new List<PackageIdentity>();

            breathFirstSearchQueue.Enqueue(packageIdentity);

            while(breathFirstSearchQueue.Count > 0)
            {
                PackageIdentity headPackage = breathFirstSearchQueue.Dequeue();
                markedPackages.Add(headPackage);

                HashSet<PackageIdentity> dependencies;
                if(uninstallationContext.RemoveDependencies && dependenciesDict.TryGetValue(headPackage, out dependencies) && dependencies != null)
                {
                    foreach(var dependency in dependencies)
                    {
                        if (markedPackages.Contains(dependency))
                        {
                            // Put it back at the end
                            markedPackages.Remove(dependency);
                            markedPackages.Add(dependency);
                        }
                        else
                        {
                            breathFirstSearchQueue.Enqueue(dependency);
                        }
                    }
                }                
            }

            return new HashSet<PackageIdentity>(markedPackages, PackageIdentity.Comparer);
        }

        private static InvalidOperationException CreatePackageHasDependentsException(PackageIdentity packageIdentity,
            List<PackageIdentity> packageDependents)
        {
            if (packageDependents.Count == 1)
            {
                return new InvalidOperationException(String.Format(CultureInfo.CurrentCulture,
                       Strings.PackageHasDependent, packageIdentity, packageDependents[0]));
            }

            return new InvalidOperationException(String.Format(CultureInfo.CurrentCulture,
                        Strings.PackageHasDependents, packageIdentity, String.Join(", ",
                        packageDependents.Select(d => d.ToString()))));
        }
    }
}
