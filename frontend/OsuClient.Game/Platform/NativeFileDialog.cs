using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace OsuClient.Game.Platform
{
    /// <summary>How a trip through the system file dialog ended.</summary>
    public readonly struct FileDialogResult
    {
        /// <summary>The file the user picked, or null if they didn't pick one.</summary>
        public string? Path { get; init; }

        /// <summary>Why the dialog couldn't be shown, when it couldn't be.</summary>
        public string? Error { get; init; }

        /// <summary>The user closed the dialog without choosing anything.</summary>
        public bool Cancelled => Path == null && Error == null;

        public static FileDialogResult Chosen(string path) => new FileDialogResult { Path = path };

        public static FileDialogResult Failed(string error) => new FileDialogResult { Error = error };

        public static FileDialogResult Cancel() => default;
    }

    /// <summary>
    /// A native "Open file" window, for the Browse button on
    /// <see cref="Screens.Generation.UploadScreen"/>.
    ///
    /// osu.Framework's own <c>GameHost.CreateSystemFileSelector</c> only
    /// returns something on the mobile hosts — on every desktop host it returns
    /// null, which is why Browse used to do nothing but tell you to drag a file
    /// in. So the desktop picker is opened here directly through the Win32
    /// common dialog, which needs no extra dependency.
    ///
    /// The dialog is modal and blocks whichever thread shows it, so it is never
    /// shown on the update thread: <see cref="OpenFile"/> hands it to a
    /// dedicated STA thread (the shell dialog requires an STA apartment) and
    /// calls back when the user is done. Getting that callback back onto the
    /// update thread is the caller's job.
    /// </summary>
    public static class NativeFileDialog
    {
        /// <summary>Whether this platform has a picker to show at all.</summary>
        public static bool IsSupported => OperatingSystem.IsWindows();

        /// <summary>
        /// Shows the picker and calls <paramref name="onCompleted"/> exactly
        /// once, from the dialog's own thread, whatever the outcome.
        /// </summary>
        /// <param name="title">Caption for the dialog window.</param>
        /// <param name="extensions">Extensions to filter to, dot-prefixed (<c>.mp3</c>).</param>
        /// <param name="initialDirectory">Where to open, or null for wherever the shell last was.</param>
        /// <param name="onCompleted">Receives the picked file, a cancellation, or a failure.</param>
        public static void OpenFile(string title, IReadOnlyList<string> extensions, string? initialDirectory,
                                    Action<FileDialogResult> onCompleted)
        {
            // Checked inline rather than through IsSupported so that the
            // platform analyser can see the Windows-only call below is guarded.
            if (!OperatingSystem.IsWindows())
            {
                onCompleted(FileDialogResult.Failed("No file picker is available on this platform."));
                return;
            }

            var thread = new Thread(() =>
            {
                FileDialogResult result;

                try
                {
                    result = showWindowsDialog(title, extensions, initialDirectory);
                }
                catch (Exception e)
                {
                    result = FileDialogResult.Failed($"Couldn't open the file picker: {e.Message}");
                }

                onCompleted(result);
            })
            {
                // The game must not be held up waiting for the dialog to close.
                IsBackground = true,
                Name = "native-file-dialog",
            };

            // The shell's dialog requires a single-threaded apartment.
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        /// <summary>
        /// Builds the comdlg32 filter: pairs of label and pattern, each
        /// null-terminated, with one more null closing the list — the string
        /// marshaller appends that one to the trailing null left here. Pure, so
        /// the part that would otherwise only be wrong in front of a user can
        /// be tested directly.
        /// </summary>
        public static string BuildFilter(IReadOnlyList<string> extensions)
        {
            string patterns = string.Join(";", extensions
                                               .Where(e => !string.IsNullOrWhiteSpace(e))
                                               .Select(e => "*" + (e.StartsWith(".", StringComparison.Ordinal) ? e : "." + e)));

            if (patterns.Length == 0)
                return "All files (*.*)\0*.*\0";

            return $"Audio files ({patterns})\0{patterns}\0All files (*.*)\0*.*\0";
        }

        private static FileDialogResult showWindowsDialog(string title, IReadOnlyList<string> extensions,
                                                          string? initialDirectory)
        {
            // Long enough for any single path the shell can hand back.
            const int buffer_characters = 32768;
            const int buffer_bytes = buffer_characters * sizeof(char);

            IntPtr fileBuffer = Marshal.AllocHGlobal(buffer_bytes);
            IntPtr filter = Marshal.StringToHGlobalUni(BuildFilter(extensions));
            IntPtr caption = Marshal.StringToHGlobalUni(title);
            IntPtr directory = initialDirectory == null ? IntPtr.Zero : Marshal.StringToHGlobalUni(initialDirectory);

            try
            {
                // GetOpenFileName reads this buffer as the initial file name, so
                // it has to start out empty rather than holding whatever was
                // last in that block of heap.
                Marshal.Copy(new byte[buffer_bytes], 0, fileBuffer, buffer_bytes);

                var name = new OpenFileName
                {
                    lpstrFilter = filter,
                    nFilterIndex = 1,
                    lpstrFile = fileBuffer,
                    nMaxFile = buffer_characters,
                    lpstrInitialDir = directory,
                    lpstrTitle = caption,
                    Flags = ofn_explorer | ofn_file_must_exist | ofn_path_must_exist
                            | ofn_no_change_dir | ofn_hide_read_only,
                };

                name.lStructSize = Marshal.SizeOf(name);

                if (!GetOpenFileNameW(ref name))
                {
                    // Zero means the user simply closed the dialog; anything
                    // else is a real failure, worth saying out loud.
                    int error = CommDlgExtendedError();

                    return error == 0
                        ? FileDialogResult.Cancel()
                        : FileDialogResult.Failed($"The file picker failed (error 0x{error:X4}).");
                }

                string? path = Marshal.PtrToStringUni(fileBuffer);

                return string.IsNullOrEmpty(path) ? FileDialogResult.Cancel() : FileDialogResult.Chosen(path);
            }
            finally
            {
                Marshal.FreeHGlobal(fileBuffer);
                Marshal.FreeHGlobal(filter);
                Marshal.FreeHGlobal(caption);

                if (directory != IntPtr.Zero)
                    Marshal.FreeHGlobal(directory);
            }
        }

        private const int ofn_hide_read_only = 0x00000004;
        private const int ofn_no_change_dir = 0x00000008;
        private const int ofn_path_must_exist = 0x00000800;
        private const int ofn_file_must_exist = 0x00001000;

        /// <summary>With no hook and no custom template, this gets the modern shell dialog rather than the Win3.1 one.</summary>
        private const int ofn_explorer = 0x00080000;

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileNameW(ref OpenFileName name);

        [DllImport("comdlg32.dll")]
        private static extern int CommDlgExtendedError();

        /// <summary>
        /// OPENFILENAMEW. Every string goes in as an already-allocated pointer
        /// so the embedded nulls in the filter survive — the default string
        /// marshaller would cut the filter off at the first one.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public IntPtr lpstrFilter;
            public IntPtr lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public IntPtr lpstrFileTitle;
            public int nMaxFileTitle;
            public IntPtr lpstrInitialDir;
            public IntPtr lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public IntPtr lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public IntPtr lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }
    }
}
