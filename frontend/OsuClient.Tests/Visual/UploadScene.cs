using System;
using System.IO;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using OsuClient.Game.Screens.Generation;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The <c>upload-empty</c>, <c>upload-loaded</c> and <c>upload-labelled</c> screenshot scenes.
    ///
    /// The loaded one goes through <see cref="UploadScreen.ChooseFile"/> — the
    /// same entry point a drop or the file dialog uses — rather than setting
    /// the cassette directly, so the frame shows the real path and cannot
    /// drift away from what the screen actually does with a file.
    /// </summary>
    public static class UploadScene
    {
        /// <summary>An empty deck, waiting for a tape.</summary>
        public static Drawable Empty() => new UploadScreen();

        /// <summary>
        /// A deck with a tape in it, two difficulties pulled out of the rack.
        ///
        /// The file is a real temporary one because the screen refuses a path
        /// that isn't there, which is the behaviour worth not bypassing.
        /// </summary>
        public static Drawable Loaded()
        {
            var screen = new UploadScreen();

            screen.OnLoadComplete += _ =>
            {
                string path = Path.Combine(Path.GetTempPath(),
                                           "midnight drive (extended mix).mp3");

                if (!File.Exists(path))
                    File.WriteAllBytes(path, Array.Empty<byte>());

                screen.ChooseFile(path);
            };

            return screen;
        }

        /// <summary>
        /// A loaded deck with cover art in its case: the first of the menu's
        /// wallpapers, which is a real picture rather than a test pattern.
        /// </summary>
        public static Drawable Covered()
        {
            var screen = (UploadScreen)Loaded();

            screen.OnLoadComplete += _ =>
            {
                string? image = Game.Graphics.WallpaperLibrary.Load(Game.Graphics.WallpaperLibrary.ResolveDefaultDirectory())
                                    .FirstOrDefault(p => Game.Backend.BackendRunner.IsSupportedCoverFile(p));

                if (image != null)
                    screen.ChooseCover(image);
            };

            return screen;
        }

        /// <summary>
        /// A loaded deck with the label written on, so the fields can be seen
        /// holding text rather than only their placeholders.
        /// </summary>
        public static Drawable Labelled()
        {
            var screen = (UploadScreen)Loaded();

            screen.OnLoadComplete += _ =>
            {
                var bay = screen.ChildrenOfType<CassetteBay>().Single();
                bay.ArtistBox.Text = "Kavinsky";
                bay.TitleBox.Text = "Nightcall (Midnight Drive Extended Mix)";
            };

            return screen;
        }
    }
}
