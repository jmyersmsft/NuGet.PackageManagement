﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NuGet.Packaging;
using NuGet.ProjectManagement;
using NuGet.Versioning;
using System.Threading;

namespace NuGet.PackageManagement.UI
{
    internal class PackageSolutionDetailControlModel : DetailControlModel
    {
        // list of projects to be displayed in the UI
        private List<PackageInstallationInfo> _packageInstallationInfos;

        private List<PackageInstallationInfo> _allProjects;

        // indicates that the model is updating the checkbox state. In this case, 
        // the CheckAllProject() & UncheckAllProject() should be no-op.
        private bool _updatingCheckbox;

        public List<PackageInstallationInfo> Projects
        {
            get
            {
                return _packageInstallationInfos;
            }
        }

        private bool _actionEnabled;

        // Indicates if the action button and preview button is enabled.
        public bool ActionEnabled
        {
            get
            {
                return _actionEnabled;
            }
            set
            {
                _actionEnabled = value;
                OnPropertyChanged("ActionEnabled");
            }
        }

        public override bool IsSolution
        {
            get
            {
                return true;
            }
        }

        protected override void OnSelectedVersionChanged()
        {
            UpdateProjectList();
        }

        private PackageReference GetInstalledPackage(NuGetProject project, string id)
        {
            var installedPackagesTask = project.GetInstalledPackagesAsync(CancellationToken.None);
            installedPackagesTask.Wait();
            var installedPackage = installedPackagesTask.Result
                .Where(p => StringComparer.OrdinalIgnoreCase.Equals(p.PackageIdentity.Id, id))
                .FirstOrDefault();
            return installedPackage;
        }

        protected override void CreateVersions()
        {
            if (SelectedAction == Resources.Action_Consolidate ||
                SelectedAction == Resources.Action_Uninstall)
            {
                _versions = _allProjects.Select(project => GetInstalledPackage(project.NuGetProject, Id))
                    .Where(package => package != null)
                    .OrderByDescending(p => p.PackageIdentity.Version)
                    .Select(package => new VersionForDisplay(package.PackageIdentity.Version, string.Empty))
                    .ToList();
            }
            else if (SelectedAction == Resources.Action_Install ||
                SelectedAction == Resources.Action_Update)
            {
                _versions = new List<VersionForDisplay>();
                var allVersions = _allPackages.OrderByDescending(v => v);
                var latestPrerelease = allVersions.FirstOrDefault(v => v.IsPrerelease);
                var latestStableVersion = allVersions.FirstOrDefault(v => !v.IsPrerelease);

                if (latestPrerelease != null && (latestStableVersion == null || latestPrerelease > latestStableVersion))
                {
                    _versions.Add(new VersionForDisplay(latestPrerelease, Resources.Version_LatestPrerelease));
                }

                if (latestStableVersion != null)
                {
                    _versions.Add(new VersionForDisplay(latestStableVersion, Resources.Version_LatestStable));
                }

                // add a separator
                if (_versions.Count > 0)
                {
                    _versions.Add(null);
                }

                foreach (var version in allVersions)
                {
                    _versions.Add(new VersionForDisplay(version, string.Empty));
                }
            }

            SelectVersion();            
            OnPropertyChanged("Versions");
        }

        public PackageSolutionDetailControlModel(IEnumerable<NuGetProject> projects) :
            base(projects)
        {
            // create project list
            _allProjects = _nugetProjects.Select(p => new PackageInstallationInfo(p, null, true))
                .ToList();
            _allProjects.Sort();
            _allProjects.ForEach(p =>
            {
                p.SelectedChanged += (sender, e) =>
                {
                    UpdateActionEnabled();
                    UpdateSelectCheckbox();
                };
            });
        }

        private void UpdateActionEnabled()
        {
            ActionEnabled =
                Projects != null &&
                Projects.Any(i => i.Selected);
        }

        private bool IsInstalled(NuGetProject project, string id)
        {
            var installedPackagesTask = project.GetInstalledPackagesAsync(CancellationToken.None);
            installedPackagesTask.Wait();
            var installed = installedPackagesTask.Result
                .Where(p => StringComparer.OrdinalIgnoreCase.Equals(p.PackageIdentity.Id, id));
            return installed.Any();
        }

        protected override bool CanInstall()
        {
            var canInstallInProjects = _nugetProjects
                .Any(project =>
                {
                    return !IsInstalled(project, Id);
                });

            return canInstallInProjects;
        }

        protected override bool CanUninstall()
        {
            var canUninstallFromProjects = _nugetProjects
                .Any(project =>
                {
                    return IsInstalled(project, Id);
                });

            return canUninstallFromProjects;
        }

        protected override bool CanUpgrade()
        {
            // In solution-level management, we don't separate upgrade from downgrade because
            // an update could be an upgrade for one project and a downgrade for another
            return false;
        }

        protected override bool CanDowngrade()
        {
            // In solution-level management, we don't separate upgrade from downgrade because
            // an update could be an upgrade for one project and a downgrade for another
            return false;
        }

        protected override bool CanUpdate()
        {
            var canUpdateInProjects = _nugetProjects
                .Any(project =>
                {
                    return IsInstalled(project, Id) && _allPackages.Count >= 2;
                });

            return canUpdateInProjects;
        }

        protected override bool CanConsolidate()
        {
            var installedVersions = _nugetProjects
                .Select(project => GetInstalledPackage(project, Id))
                .Where(package => package != null)
                .Select(package => package.PackageIdentity.Version)
                .Distinct();
            return installedVersions.Count() >= 2;
        }

        private void UpdateProjectList()
        {
            // update properties of _allProject list
            _allProjects.ForEach(p =>
            {
                var installed = GetInstalledPackage(p.NuGetProject, Id);
                if (installed != null)
                {
                    p.Version = installed.PackageIdentity.Version;
                }
                else
                {
                    p.Version = null;
                }
            });


            if (SelectedAction == Resources.Action_Consolidate)
            {
                // only projects that have the package installed, but with a
                // different version, are enabled.
                // The project with the same version installed is not enabled.
                _allProjects.ForEach(p =>
                {
                    var installed = GetInstalledPackage(p.NuGetProject, Id);
                    p.Enabled = installed != null &&
                        installed.PackageIdentity.Version != SelectedVersion.Version;
                    p.Selected = p.Enabled;
                });
            }
            else if (SelectedAction == Resources.Action_Update)
            {
                // only projects that have the package of a different version installed are enabled
                _allProjects.ForEach(p =>
                {
                    var installed = GetInstalledPackage(p.NuGetProject, Id);
                    p.Enabled = installed != null &&
                        installed.PackageIdentity.Version != SelectedVersion.Version;
                    p.Selected = p.Enabled;
                });
            }
            else if (SelectedAction == Resources.Action_Install)
            {
                // only projects that do not have the package installed are enabled
                _allProjects.ForEach(p =>
                {
                    var installed = GetInstalledPackage(p.NuGetProject, Id);
                    p.Enabled = installed == null;
                    p.Selected = p.Enabled;
                });
            }
            else if (SelectedAction == Resources.Action_Uninstall)
            {
                // only projects that have the selected version installed are enabled
                _allProjects.ForEach(p =>
                {
                    var installed = GetInstalledPackage(p.NuGetProject, Id);
                    p.Enabled = installed != null &&
                        installed.PackageIdentity.Version == SelectedVersion.Version;
                    p.Selected = p.Enabled;
                });
            }

            if (ShowAll)
            {
                _packageInstallationInfos = _allProjects;
            }
            else
            {
                _packageInstallationInfos = _allProjects.Where(p => p.Enabled).ToList();
            }

            UpdateActionEnabled();
            UpdateSelectCheckbox();
            OnPropertyChanged("Projects");
        }

        private bool? _checkboxState;

        public bool? CheckboxState
        {
            get
            {
                return _checkboxState;
            }
            set
            {
                _checkboxState = value;
                OnPropertyChanged("CheckboxState");
            }
        }

        private string _selectCheckboxText;

        // The text of the project selection checkbox
        public string SelectCheckboxText
        {
            get
            {
                return _selectCheckboxText;
            }
            set
            {
                _selectCheckboxText = value;
                OnPropertyChanged("SelectCheckboxText");
            }
        }

        private void UpdateSelectCheckbox()
        {
            if (Projects == null)
            {
                return;
            }

            _updatingCheckbox = true;
            var countTotal = Projects.Count(p => p.Enabled);

            SelectCheckboxText = string.Format(
                CultureInfo.CurrentCulture,
                Resources.Checkbox_ProjectSelection,
                countTotal);

            var countSelected = Projects.Count(p => p.Selected);
            if (countSelected == 0)
            {
                CheckboxState = false;
            }
            else if (countSelected == countTotal)
            {
                CheckboxState = true;
            }
            else
            {
                CheckboxState = null;
            }
            _updatingCheckbox = false;
        }

        internal void UncheckAllProjects()
        {
            if (_updatingCheckbox)
            {
                return;
            }

            Projects.ForEach(p =>
            {
                if (p.Enabled)
                {
                    p.Selected = false;
                }
            });
        }

        internal void CheckAllProjects()
        {
            if (_updatingCheckbox)
            {
                return;
            }

            Projects.ForEach(p =>
            {
                if (p.Enabled)
                {
                    p.Selected = true;
                }
            });

            OnPropertyChanged("Projects");
        }

        private bool _showAll;

        // The checked state of the Show All check box
        public bool ShowAll
        {
            get
            {
                return _showAll;
            }
            set
            {
                _showAll = value;

                UpdateProjectList();
            }
        }

        public override IEnumerable<NuGetProject> SelectedProjects
        {
            get
            {
                return _allProjects.Where(p => p.Selected)
                    .Select(p => p.NuGetProject);
            }
        }
    }
}
