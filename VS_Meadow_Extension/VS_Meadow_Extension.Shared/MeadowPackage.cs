﻿using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using Microsoft.VisualStudio.Shell;

using Meadow.CLI.Core.DeviceManagement;
using Meadow.Helpers;
using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace Meadow
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#1110", "#1112", Globals.AssemblyVersion, IconResourceID = 1400)] // Info on this package for Help/About
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(GuidList.guidMeadowPackageString)]
    public sealed class MeadowPackage : AsyncPackage
    {
        private const string NoDevicesFound = "No Devices Found";

        public static bool DebugOrDeployInProgress { get; set; } = false;

        private Lazy<MeadowSettings> meadowSettingsLazy;
        private MeadowSettings MeadowSettings => meadowSettingsLazy.Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="MeadowPackage"/> class.
        /// </summary>
        public MeadowPackage()
        {
            // Inside this method you can place any initialization code that does not require
            // any Visual Studio service because at this point the package object is created but
            // not sited yet inside Visual Studio environment. The place to do all the other
            // initialization is the Initialize method.
        }

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            // Make settings loading lazy
            meadowSettingsLazy = new Lazy<MeadowSettings>(() => new MeadowSettings(Globals.SettingsFilePath));

            // Ensure Install dependencies is off loaded to a background thread
            _ = InstallDependencies(cancellationToken);

            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Add our command handlers for menu (commands must be declared in the .vsct file)
            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService mcs)
            {
                CommandID menuMeadowDeviceListComboCommandID = new CommandID(GuidList.guidMeadowPackageCmdSet, (int)PkgCmdIDList.cmdidMeadowDeviceListCombo);
                OleMenuCommand menuMeadowDeviceListComboCommand = new OleMenuCommand(new EventHandler(OnMeadowDeviceListCombo), menuMeadowDeviceListComboCommandID);
                mcs.AddCommand(menuMeadowDeviceListComboCommand);

                CommandID menuMeadowDeviceListComboGetListCommandID = new CommandID(GuidList.guidMeadowPackageCmdSet, (int)PkgCmdIDList.cmdidMeadowDeviceListComboGetList);
                MenuCommand menuMeadowDeviceListComboGetListCommand = new OleMenuCommand(new EventHandler(OnMeadowDeviceListComboGetList), menuMeadowDeviceListComboGetListCommandID);
                mcs.AddCommand(menuMeadowDeviceListComboGetListCommand);
            }
        }
        #endregion

        private async void OnMeadowDeviceListCombo(object sender, EventArgs e)
        {
            if (!DebugOrDeployInProgress)
            {
                if (e is OleMenuCmdEventArgs eventArgs)
                {
                    var portList = await MeadowDeviceManager.GetSerialPorts();

                    IntPtr vOut = eventArgs.OutValue;

                    // when vOut is non-NULL, the IDE is requesting the current value for the combo
                    if (vOut != IntPtr.Zero)
                    {
                        if (portList.Count > 0)
                        {
                            string deviceTarget = string.Empty;

                            bool IsSavedValueInPortList = IsValueInPortList(portList, MeadowSettings.DeviceTarget);
                            if (IsSavedValueInPortList)
                            {
                                deviceTarget = MeadowSettings.DeviceTarget;
                            }

                            Marshal.GetNativeVariantForObject(deviceTarget, vOut);
                        }
                        else
                        {
                            Marshal.GetNativeVariantForObject(NoDevicesFound, vOut);
                        }
                    }
                    else if (eventArgs.InValue is string newChoice)
                    {
                        // new value was selected check if it is in our list
                        bool valueInPortList = IsValueInPortList(portList, newChoice);

                        if (valueInPortList)
                        {
                            SaveDeviceChoiceToSettings(newChoice);
                        }
                        else
                        {
                            if (!newChoice.Equals(NoDevicesFound))
                            {
                                throw (new ArgumentException("Invalid Device Selected"));
                            }
                        }
                    }
                }
                else
                {
                    // We should never get here; EventArgs are required.
                    throw (new ArgumentException("EventArgs Required")); // force an exception to be thrown
                }
            }
        }

        private async void OnMeadowDeviceListComboGetList(object sender, EventArgs e)
        {
            if (!DebugOrDeployInProgress)
            {
                if (e is OleMenuCmdEventArgs eventArgs)
                {
                    object inParam = eventArgs.InValue;
                    IntPtr vOut = eventArgs.OutValue;

                    if (inParam != null)
                    {
                        throw (new ArgumentException("InParam Invalid")); // force an exception to be thrown
                    }
                    else if (vOut != IntPtr.Zero)
                    {
                        var portList = await MeadowDeviceManager.GetSerialPorts();
                        if (portList.Count > 0)
                        {
                            Marshal.GetNativeVariantForObject(portList, vOut);
                        }
                        else
                        {
                            Marshal.GetNativeVariantForObject(new string[] { NoDevicesFound }, vOut);
                        }
                    }
                    else
                    {
                        throw (new ArgumentException("OutParam Required")); // force an exception to be thrown
                    }
                }
            }
        }

        private static bool IsValueInPortList(IList<string> portList, string newChoice)
        {
            bool validInput = false;
            for (int i = 0; i < portList.Count; i++)
            {
                if (string.Compare(portList[i], newChoice, StringComparison.CurrentCultureIgnoreCase) == 0)
                {
                    validInput = true;
                    break;
                }
            }

            return validInput;
        }

        private void SaveDeviceChoiceToSettings(string newChoice)
        {
            MeadowSettings.DeviceTarget = newChoice;
            MeadowSettings.Save();
        }

        private async Task InstallDependencies(CancellationToken cancellationToken)
        {
            // No point installing if we don't have an internet connection
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return;
            }

            //string templateName = "Meadow";
            // Check if the package is installed
            //if (!await IsTemplateInstalled(templateName))
            {
                string packageName = "WildernessLabs.Meadow.Template";

                // Install the package.
                // If an update is available it should update it automagically.
                if (!await InstallPackage(packageName, cancellationToken))
                {
                    // Unable to install ProjectTemplates Throw Up a Message??
                }
            }

        }

        private async Task<bool> InstallPackage(string packageName, CancellationToken cancellationToken)
        {
            return await StartDotNetProcess("new install", packageName, cancellationToken);
        }

        private async Task<bool> IsTemplateInstalled(string templateName, CancellationToken cancellationToken)
        {
            return await StartDotNetProcess("new list", templateName, cancellationToken);
        }

        private async Task<bool> StartDotNetProcess(string command, string parameters, CancellationToken cancellationToken)
        {
            using (var process = new System.Diagnostics.Process())
            {
                process.StartInfo.FileName = "dotnet";
                process.StartInfo.Arguments = $"{command} {parameters}";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                var outputBuilder = string.Empty;
                var errorBuilder = string.Empty;

                // Event handlers for async output reading
                process.OutputDataReceived += (sender, args) => outputBuilder += Environment.NewLine + args.Data;
                process.ErrorDataReceived += (sender, args) => errorBuilder += Environment.NewLine + args.Data;

                process.Start();

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for the process to complete asynchronously
                await Task.Run(() =>
                {
                    process.WaitForExit();
                }, cancellationToken);

                /* TODO Maybe we should log the output and error to a file 
                var output = outputBuilder.ToString();
                var errorOutput = errorBuilder.ToString(); */

                if (process.ExitCode == 0)
                {
                    // Process completed successfully
                    return true;
                }
                else
                {
                    // Process failed
                    return false;
                }
            }
        }
    }

    static class GuidList
    {
        /// <summary>
        /// MeadowPackage GUID string.
        /// </summary>
        public const string guidMeadowPackageString = "9e640b9d-2a9e-4da3-ba5e-351adc854fd2";
        public const string guidMeadowPackageCmdSetString = "0af06414-3c09-44ff-88a1-c4e1a35b0bdf";

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        public static readonly Guid guidMeadowPackage = new Guid(guidMeadowPackageString);
        public static readonly Guid guidMeadowPackageCmdSet = new Guid(guidMeadowPackageCmdSetString);
    }

    static class PkgCmdIDList
    {
        public const uint cmdidMeadowDeviceListCombo = 0x101;
        public const uint cmdidMeadowDeviceListComboGetList = 0x102;
    }
}