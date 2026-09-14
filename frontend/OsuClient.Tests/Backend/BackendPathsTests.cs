using System;
using System.IO;
using NUnit.Framework;
using OsuClient.Game.Backend;

namespace OsuClient.Tests.Backend
{
    /// <summary>
    /// Pure path resolution against a fake checkout — no Python involved, so
    /// "would this build find a usable backend" is answerable without one
    /// being installed.
    /// </summary>
    [TestFixture]
    public class BackendPathsTests
    {
        private string root = null!;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "osuclient-backend-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        /// <summary>Writes the pipeline entry point the resolver looks for.</summary>
        private void createMainScript()
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, "src", "main.py"), "# fake");
        }

        /// <summary>
        /// Creates an interpreter inside the named virtual environment, in
        /// whichever layout this OS actually uses, and returns its path.
        /// </summary>
        private string createInterpreter(string environmentName)
        {
            string directory = OperatingSystem.IsWindows()
                ? Path.Combine(root, environmentName, "Scripts")
                : Path.Combine(root, environmentName, "bin");

            Directory.CreateDirectory(directory);

            string executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "python.exe" : "python");
            File.WriteAllText(executable, string.Empty);

            return executable;
        }

        [Test]
        public void LocatesInterpreterAndScriptInACompleteCheckout()
        {
            createMainScript();
            string expected = createInterpreter(".venv");

            var paths = BackendPaths.Locate(root, out string error);

            Assert.Multiple(() =>
            {
                Assert.That(paths, Is.Not.Null);
                Assert.That(error, Is.Empty);
                Assert.That(paths!.PythonExecutable, Is.EqualTo(expected));
                Assert.That(paths.MainScript, Is.EqualTo(Path.Combine(root, "src", "main.py")));
                Assert.That(paths.OutputDirectory, Is.EqualTo(Path.Combine(root, "data", "output")));
                Assert.That(paths.RepositoryRoot, Is.EqualTo(root));
            });
        }

        [Test]
        public void PrefersDotVenvOverAStaleVenv()
        {
            // This repository carries both; the plain venv/ is the stale one,
            // and picking it would fail deep inside an import instead of here.
            createMainScript();
            string dotVenv = createInterpreter(".venv");
            createInterpreter("venv");

            var paths = BackendPaths.Locate(root, out _);

            Assert.That(paths!.PythonExecutable, Is.EqualTo(dotVenv));
        }

        [Test]
        public void FallsBackToVenvWhenDotVenvIsAbsent()
        {
            createMainScript();
            string venv = createInterpreter("venv");

            var paths = BackendPaths.Locate(root, out _);

            Assert.That(paths!.PythonExecutable, Is.EqualTo(venv));
        }

        [Test]
        public void ReportsAMissingVirtualEnvironmentRatherThanThrowing()
        {
            createMainScript();

            var paths = BackendPaths.Locate(root, out string error);

            Assert.Multiple(() =>
            {
                Assert.That(paths, Is.Null);
                Assert.That(error, Does.Contain("virtual environment"));
            });
        }

        [Test]
        public void ReportsAMissingPipelineScript()
        {
            createInterpreter(".venv");

            var paths = BackendPaths.Locate(root, out string error);

            Assert.Multiple(() =>
            {
                Assert.That(paths, Is.Null);
                Assert.That(error, Does.Contain("main.py"));
            });
        }

        [Test]
        public void ReportsAMissingCheckoutForANullOrUnknownRoot()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BackendPaths.Locate(null, out string nullError), Is.Null);
                Assert.That(nullError, Is.Not.Empty);

                Assert.That(BackendPaths.Locate(Path.Combine(root, "nope"), out string missingError), Is.Null);
                Assert.That(missingError, Is.Not.Empty);
            });
        }
    }
}
