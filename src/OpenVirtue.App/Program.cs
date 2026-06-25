// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine;
using OpenVirtue.Formats.Wrs;

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
                "Controls: WASD move, Q/E down/up, hold Shift to go faster, drag left mouse to look.",
                "OpenVirtue",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            string archivePath = args[0];
            string mainWdl = args.Length >= 2 ? args[1] : Path.GetFileNameWithoutExtension(archivePath) + ".WDL";
            Level level = LevelLoader.Load(WrsArchive.ReadFile(archivePath), mainWdl);

            Application.Run(new LevelWindow(level));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "OpenVirtue — error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
