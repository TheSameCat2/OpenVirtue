// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Length < 1)
        {
            MessageBox.Show(
                "Usage: OpenVirtue.App <archive.wrs> [main.wdl]\n\n" +
                "Point it at one of your own Saints of Virtue .WRS files (e.g. apathy.wrs).\n" +
                "Controls: WASD walk, Space jump, hold Shift to run, hold Ctrl to creep, move mouse to look.",
                "OpenVirtue",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            string archivePath = args[0];
            string mainWdl = args.Length >= 2 ? args[1] : Path.GetFileNameWithoutExtension(archivePath) + ".WDL";

            Application.Run(new LevelWindow(archivePath, mainWdl));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "OpenVirtue — error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
