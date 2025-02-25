﻿// <copyright file="RowData.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SystemTrayMenu.DataClasses
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Data;
    using System.Diagnostics;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Security;
    using System.Text;
    using System.Windows.Forms;
    using IWshRuntimeLibrary;
    using SystemTrayMenu.Utilities;
    using TAFactory.IconPack;
    using Menu = SystemTrayMenu.UserInterface.Menu;

    internal class RowData : IDisposable
    {
        private static readonly Icon White50PercentageIcon = Properties.Resources.White50Percentage;
        private static DateTime contextMenuClosed;
        private string workingDirectory;
        private string arguments;
        private string text;
        private Icon icon;
        private bool diposeIcon = true;
        private bool isDisposed;

        internal RowData()
        {
        }

        internal FileInfo FileInfo { get; set; }

        internal Menu SubMenu { get; set; }

        internal bool IsMenuOpen { get; set; }

        internal bool IsSelected { get; set; }

        internal bool ContainsMenu { get; set; }

        internal bool IsContextMenuOpen { get; set; }

        internal bool IsResolvedLnk { get; set; }

        internal bool HiddenEntry { get; set; }

        internal string TargetFilePath { get; set; }

        internal string TargetFilePathOrig { get; set; }

        internal int RowIndex { get; set; }

        internal int MenuLevel { get; set; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        internal void SetText(string text)
        {
            this.text = text;
        }

        internal void SetData(RowData data, DataTable dataTable)
        {
            DataRow row = dataTable.Rows.Add();
            data.RowIndex = dataTable.Rows.IndexOf(row);

            if (icon == null)
            {
                icon = White50PercentageIcon;
            }

            if (HiddenEntry)
            {
                row[0] = IconReader.AddIconOverlay(
                    data.icon,
                    White50PercentageIcon);
            }
            else
            {
                row[0] = data.icon;
            }

            if (!ContainsMenu &&
                Config.IsHideFileExtension())
            {
                row[1] = Path.GetFileNameWithoutExtension(data.text);
            }
            else
            {
                row[1] = data.text;
            }

            row[2] = data;
        }

        internal bool ReadIcon(bool isDirectory, ref string resolvedLnkPath)
        {
            bool isLnkDirectory = false;

            if (string.IsNullOrEmpty(TargetFilePath))
            {
                Log.Info($"TargetFilePath from {resolvedLnkPath} empty");
            }
            else if (isDirectory)
            {
                icon = IconReader.GetFolderIconSTA(
                    TargetFilePath,
                    IconReader.FolderType.Closed,
                    false);
            }
            else
            {
                bool handled = false;
                string fileExtension = Path.GetExtension(TargetFilePath);

                if (fileExtension == ".lnk")
                {
                    handled = SetLnk(
                        ref isLnkDirectory,
                        ref resolvedLnkPath);
                }
                else if (fileExtension == ".url")
                {
                    handled = SetUrl();
                }
                else if (fileExtension == ".sln")
                {
                    handled = SetSln();
                }

                if (!handled)
                {
                    try
                    {
                        icon = IconReader.GetFileIconWithCache(TargetFilePath, false);
                        diposeIcon = false;

                        // other project -> fails sometimes
                        // icon = IconHelper.ExtractIcon(TargetFilePath, 0);

                        // standard way -> fails sometimes
                        // icon = Icon.ExtractAssociatedIcon(filePath);

                        // API Code Pack  -> fails sometimes
                        // ShellFile shellFile = ShellFile.FromFilePath(filePath);
                        // Bitmap shellThumb = shellFile.Thumbnail.ExtraLargeBitmap;

                        // IShellItemImageFactory GetImage works, but missing link overlay there #149
                    }
                    catch (Exception ex)
                    {
                        if (ex is SecurityException ||
                            ex is ArgumentException ||
                            ex is UnauthorizedAccessException ||
                            ex is PathTooLongException ||
                            ex is NotSupportedException)
                        {
                            Log.Warn($"path:'{TargetFilePath}'", ex);
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }

            return isLnkDirectory;
        }

        internal void MouseDown(DataGridView dgv, MouseEventArgs e)
        {
            if (e != null &&
                e.Button == MouseButtons.Right &&
                FileInfo != null &&
                dgv != null &&
                dgv.Rows.Count > RowIndex &&
                (DateTime.Now - contextMenuClosed).TotalMilliseconds > 200)
            {
                IsContextMenuOpen = true;

                ShellContextMenu ctxMnu = new ShellContextMenu();
                Point location = dgv.FindForm().Location;
                Point point = new Point(
                    e.X + location.X + dgv.Location.X,
                    e.Y + location.Y + dgv.Location.Y);
                if (ContainsMenu)
                {
                    DirectoryInfo[] dir = new DirectoryInfo[1];
                    dir[0] = new DirectoryInfo(TargetFilePathOrig);
                    ctxMnu.ShowContextMenu(dir, point);
                }
                else
                {
                    FileInfo[] arrFI = new FileInfo[1];
                    arrFI[0] = new FileInfo(TargetFilePathOrig);
                    ctxMnu.ShowContextMenu(arrFI, point);
                }

                IsContextMenuOpen = false;
                contextMenuClosed = DateTime.Now;
            }

            if (Properties.Settings.Default.OpenItemWithOneClick)
            {
                OpenItem(e);
            }
        }

        internal void DoubleClick(MouseEventArgs e)
        {
            if (!Properties.Settings.Default.OpenItemWithOneClick)
            {
                OpenItem(e);
            }

            if (ContainsMenu &&
                (e == null || e.Button == MouseButtons.Left))
            {
                Log.ProcessStart(TargetFilePath, null, true);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (diposeIcon)
                {
                    icon?.Dispose();
                }
            }

            isDisposed = true;
        }

        private void OpenItem(MouseEventArgs e)
        {
            if (!ContainsMenu &&
                (e == null || e.Button == MouseButtons.Left))
            {
                try
                {
                    using Process p = new Process
                    {
                        StartInfo = new ProcessStartInfo(TargetFilePath)
                        {
                            FileName = TargetFilePathOrig,
                            Arguments = arguments,
                            WorkingDirectory = workingDirectory,
                            CreateNoWindow = true,
                            UseShellExecute = true,
                        },
                    };
                    p.Start();
                }
                catch (Win32Exception ex)
                {
                    Log.Warn($"path:'{TargetFilePath}'", ex);
                    MessageBox.Show(ex.Message);
                }
            }
        }

        private bool SetLnk(
            ref bool isLnkDirectory,
            ref string resolvedLnkPath)
        {
            bool handled = false;
            resolvedLnkPath = FileLnk.GetResolvedFileName(TargetFilePath);
            if (FileLnk.IsDirectory(resolvedLnkPath))
            {
                icon = IconReader.GetFolderIconSTA(TargetFilePath, IconReader.FolderType.Open, true);
                handled = true;
                isLnkDirectory = true;
            }
            else if (FileLnk.IsNetworkRoot(resolvedLnkPath))
            {
                isLnkDirectory = true;
            }
            else if (string.IsNullOrEmpty(resolvedLnkPath))
            {
                Log.Info($"Resolve *.LNK '{TargetFilePath}' has no icon");
            }
            else
            {
                IWshShell shell = new WshShell();
                IWshShortcut lnk = shell.CreateShortcut(TargetFilePath)
                    as IWshShortcut;
                arguments = lnk.Arguments;
                workingDirectory = lnk.WorkingDirectory;
                string iconLocation = lnk.IconLocation;
                if (iconLocation.Length > 2)
                {
                    iconLocation = iconLocation[0..^2];
                    if (System.IO.File.Exists(iconLocation))
                    {
                        try
                        {
                            icon = Icon.ExtractAssociatedIcon(iconLocation);
                            handled = true;
                        }
                        catch (ArgumentException ex)
                        {
                            Log.Warn($"iconLocation:'{iconLocation}'", ex);
                        }
                    }
                }

                TargetFilePath = resolvedLnkPath;
            }

            SetText(Path.GetFileNameWithoutExtension(TargetFilePathOrig));

            return handled;
        }

        private bool SetUrl()
        {
            bool handled = false;
            string iconFile = string.Empty;
            try
            {
                FileIni file = new FileIni(TargetFilePath);
                iconFile = file.Value("IconFile", string.Empty);
                if (string.IsNullOrEmpty(iconFile))
                {
                    string browserPath = FileUrl.GetDefaultBrowserPath();
                    if (string.IsNullOrEmpty(browserPath))
                    {
                        Log.Info($"Resolve *.URL '{TargetFilePath}'" +
                            $"No default browser found!");
                    }
                    else
                    {
                        icon = IconReader.GetFileIconWithCache(browserPath, false);
                        diposeIcon = false;
                        handled = true;
                    }
                }
                else if (System.IO.File.Exists(iconFile))
                {
                    icon = Icon.ExtractAssociatedIcon(iconFile);
                    handled = true;
                }
                else
                {
                    Log.Info($"Resolve *.URL '{TargetFilePath}' has no icon");
                }
            }
            catch (Exception ex)
            {
                if (ex is SecurityException ||
                    ex is ArgumentException ||
                    ex is UnauthorizedAccessException ||
                    ex is PathTooLongException ||
                    ex is NotSupportedException)
                {
                    Log.Warn(
                        $"path:'{TargetFilePath}', " +
                        $"iconFile:'{iconFile}'",
                        ex);
                }
                else
                {
                    throw;
                }
            }

            SetText($"{FileInfo.Name[0..^4]}");

            return handled;
        }

        private bool SetSln()
        {
            bool handled = false;
            StringBuilder executable = new StringBuilder(1024);
            try
            {
                DllImports.NativeMethods.Shell32FindExecutable(TargetFilePath, string.Empty, executable);

                // icon = IconReader.GetFileIcon(executable, false);
                // e.g. VS 2019 icon, need another icom in imagelist
                List<Icon> extractedIcons = IconHelper.ExtractAllIcons(
                    executable.ToString());
                icon = extractedIcons.Last();
                handled = true;
            }
            catch (Exception ex)
            {
                if (ex is SecurityException ||
                    ex is ArgumentException ||
                    ex is UnauthorizedAccessException ||
                    ex is PathTooLongException ||
                    ex is NotSupportedException)
                {
                    Log.Warn(
                        $"path:'{TargetFilePath}', " +
                        $"executable:'{executable}'",
                        ex);
                }
                else
                {
                    throw;
                }
            }

            return handled;
        }
    }
}
