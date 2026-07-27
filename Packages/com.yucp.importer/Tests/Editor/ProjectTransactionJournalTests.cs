using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class ProjectTransactionJournalTests
    {
        [Test]
        public void ApplyCommitsOnlyPreverifiedStagingFiles()
        {
            string root = CreateScratch();
            try
            {
                string project = Path.Combine(root, "project");
                string staging = Path.Combine(root, "staging");
                Directory.CreateDirectory(Path.Combine(project, "Assets", "Product"));
                Directory.CreateDirectory(Path.Combine(staging, "Assets", "Product"));
                File.WriteAllText(
                    Path.Combine(project, "Assets", "Product", "file.txt"),
                    "prior");
                string stagedPath = Path.Combine(staging, "Assets", "Product", "file.txt");
                File.WriteAllText(stagedPath, "verified");

                ProjectTransactionResult result = ProjectTransactionJournal.Apply(
                    project,
                    staging,
                    "run-1",
                    new[]
                    {
                        new VerifiedStagingFile
                        {
                            bytes = new FileInfo(stagedPath).Length,
                            normalizedPath = "Assets/Product/file.txt",
                            sha256 = Sha256(stagedPath),
                        },
                    });

                Assert.That(result.state, Is.EqualTo("committed"));
                Assert.That(
                    File.ReadAllText(Path.Combine(project, "Assets", "Product", "file.txt")),
                    Is.EqualTo("verified"));
                Assert.That(File.Exists(result.journalPath), Is.True);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ApplyRejectsCorruptStagingBeforeLiveMutation()
        {
            string root = CreateScratch();
            try
            {
                string project = Path.Combine(root, "project");
                string staging = Path.Combine(root, "staging");
                Directory.CreateDirectory(Path.Combine(project, "Assets", "Product"));
                Directory.CreateDirectory(Path.Combine(staging, "Assets", "Product"));
                string livePath = Path.Combine(project, "Assets", "Product", "file.txt");
                string stagedPath = Path.Combine(staging, "Assets", "Product", "file.txt");
                File.WriteAllText(livePath, "prior");
                File.WriteAllText(stagedPath, "substituted");

                Assert.Throws<CryptographicException>(() =>
                    ProjectTransactionJournal.Apply(
                        project,
                        staging,
                        "run-2",
                        new[]
                        {
                            new VerifiedStagingFile
                            {
                                bytes = new FileInfo(stagedPath).Length,
                                normalizedPath = "Assets/Product/file.txt",
                                sha256 = new string('0', 64),
                            },
                        }));

                Assert.That(File.ReadAllText(livePath), Is.EqualTo("prior"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ApplyRemovesOnlyUnchangedObsoleteOwnedFiles()
        {
            string root = CreateScratch();
            try
            {
                string project = Path.Combine(root, "project");
                string staging = Path.Combine(root, "staging");
                string product = Path.Combine(project, "Assets", "Product");
                Directory.CreateDirectory(product);
                Directory.CreateDirectory(Path.Combine(staging, "Assets", "Product"));
                string retained = Path.Combine(product, "retained.txt");
                string obsolete = Path.Combine(product, "obsolete.txt");
                string modified = Path.Combine(product, "modified.txt");
                File.WriteAllText(retained, "version one");
                File.WriteAllText(obsolete, "owned");
                File.WriteAllText(modified, "user change");
                string stagedRetained = Path.Combine(
                    staging,
                    "Assets",
                    "Product",
                    "retained.txt");
                File.WriteAllText(stagedRetained, "version two");

                ProjectTransactionJournal.Apply(
                    project,
                    staging,
                    "run-update",
                    new[]
                    {
                        Record(stagedRetained, "Assets/Product/retained.txt"),
                    },
                    new[]
                    {
                        Record(retained, "Assets/Product/retained.txt"),
                        new VerifiedStagingFile
                        {
                            bytes = new FileInfo(obsolete).Length,
                            normalizedPath = "Assets/Product/obsolete.txt",
                            sha256 = Sha256(obsolete),
                        },
                        new VerifiedStagingFile
                        {
                            bytes = 5,
                            normalizedPath = "Assets/Product/modified.txt",
                            sha256 = Sha256Text("owned"),
                        },
                    });

                Assert.That(File.ReadAllText(retained), Is.EqualTo("version two"));
                Assert.That(File.Exists(obsolete), Is.False);
                Assert.That(File.ReadAllText(modified), Is.EqualTo("user change"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void RecoverCommitsAPreparedTransaction()
        {
            string root = CreateScratch();
            try
            {
                string project = Path.Combine(root, "project");
                string staging = Path.Combine(root, "staging");
                Directory.CreateDirectory(Path.Combine(project, "Assets", "Product"));
                Directory.CreateDirectory(Path.Combine(staging, "Assets", "Product"));
                string livePath = Path.Combine(project, "Assets", "Product", "file.txt");
                string stagedPath = Path.Combine(staging, "Assets", "Product", "file.txt");
                File.WriteAllText(livePath, "prior");
                File.WriteAllText(stagedPath, "verified");

                ProjectTransactionResult prepared = ProjectTransactionJournal.Prepare(
                    project,
                    staging,
                    "run-recover",
                    new[] { Record(stagedPath, "Assets/Product/file.txt") });
                Assert.That(prepared.state, Is.EqualTo("prepared"));
                Assert.That(File.ReadAllText(livePath), Is.EqualTo("prior"));

                ProjectTransactionResult recovered = ProjectTransactionJournal.Recover(
                    project,
                    "run-recover");

                Assert.That(recovered.state, Is.EqualTo("committed"));
                Assert.That(File.ReadAllText(livePath), Is.EqualTo("verified"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void RollBackCommittedRestoresPriorFiles()
        {
            string root = CreateScratch();
            try
            {
                string project = Path.Combine(root, "project");
                string staging = Path.Combine(root, "staging");
                Directory.CreateDirectory(
                    Path.Combine(project, "Assets", "Product"));
                Directory.CreateDirectory(
                    Path.Combine(staging, "Assets", "Product"));
                string livePath = Path.Combine(
                    project,
                    "Assets",
                    "Product",
                    "file.txt");
                string stagedPath = Path.Combine(
                    staging,
                    "Assets",
                    "Product",
                    "file.txt");
                File.WriteAllText(livePath, "prior");
                File.WriteAllText(stagedPath, "verified");
                ProjectTransactionJournal.Apply(
                    project,
                    staging,
                    "run-post-import-failure",
                    new[]
                    {
                        Record(
                            stagedPath,
                            "Assets/Product/file.txt"),
                    });

                ProjectTransactionResult result =
                    ProjectTransactionJournal.RollBackCommitted(
                        project,
                        "run-post-import-failure");

                Assert.That(result.state, Is.EqualTo("rolled-back"));
                Assert.That(File.ReadAllText(livePath), Is.EqualTo("prior"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void InspectReportsCommittedPackageDescriptorChanges()
        {
            string root = CreateScratch();
            try
            {
                string project = Path.Combine(root, "project");
                string staging = Path.Combine(root, "staging");
                string normalizedPath =
                    "Packages/com.example.product/package.json";
                string stagedPath = Path.Combine(
                    staging,
                    normalizedPath.Replace(
                        '/',
                        Path.DirectorySeparatorChar));
                Directory.CreateDirectory(project);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath));
                File.WriteAllText(stagedPath, "{\"name\":\"com.example.product\"}");
                ProjectTransactionJournal.Apply(
                    project,
                    staging,
                    "run-inspect",
                    new[] { Record(stagedPath, normalizedPath) });

                ProjectTransactionInspection inspection =
                    ProjectTransactionJournal.Inspect(
                        project,
                        "run-inspect");

                Assert.That(inspection.state, Is.EqualTo("committed"));
                Assert.IsTrue(inspection.requiresPackageResolution);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void RemoveOwnedFilesPreservesModifiedContent()
        {
            string root = CreateScratch();
            try
            {
                string project = Path.Combine(root, "project");
                string product = Path.Combine(project, "Assets", "Product");
                Directory.CreateDirectory(product);
                string unchanged = Path.Combine(product, "unchanged.txt");
                string modified = Path.Combine(product, "modified.txt");
                string unrelated = Path.Combine(product, "user-note.txt");
                File.WriteAllText(unchanged, "owned");
                File.WriteAllText(modified, "user change");
                File.WriteAllText(unrelated, "not owned");

                ProjectTransactionResult result =
                    ProjectTransactionJournal.RemoveOwnedFiles(
                        project,
                        "run-uninstall",
                        new[]
                        {
                            new VerifiedStagingFile
                            {
                                bytes = 5,
                                normalizedPath = "Assets/Product/unchanged.txt",
                                sha256 = Sha256Text("owned"),
                            },
                            new VerifiedStagingFile
                            {
                                bytes = 5,
                                normalizedPath = "Assets/Product/modified.txt",
                                sha256 = Sha256Text("owned"),
                            },
                        });

                Assert.That(result.state, Is.EqualTo("committed"));
                Assert.That(File.Exists(unchanged), Is.False);
                Assert.That(File.ReadAllText(modified), Is.EqualTo("user change"));
                Assert.That(File.ReadAllText(unrelated), Is.EqualTo("not owned"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ApplyRejectsAConcurrentProjectMutation()
        {
            string root = CreateScratch();
            try
            {
                string project = Path.Combine(root, "project");
                string staging = Path.Combine(root, "staging");
                Directory.CreateDirectory(Path.Combine(project, "Assets", "Product"));
                Directory.CreateDirectory(Path.Combine(staging, "Assets", "Product"));
                string stagedPath = Path.Combine(staging, "Assets", "Product", "file.txt");
                File.WriteAllText(stagedPath, "verified");
                string lockDirectory = Path.Combine(project, ".yucp", "locks");
                Directory.CreateDirectory(lockDirectory);
                using (var held = new FileStream(
                    Path.Combine(lockDirectory, "package-lifecycle.lock"),
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None))
                {
                    Assert.Throws<IOException>(() =>
                        ProjectTransactionJournal.Apply(
                            project,
                            staging,
                            "run-concurrent",
                            new[]
                            {
                                Record(
                                    stagedPath,
                                    "Assets/Product/file.txt"),
                            }));
                }
                Assert.That(
                    File.Exists(
                        Path.Combine(project, "Assets", "Product", "file.txt")),
                    Is.False);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ApplyReadsVerifiedStagingFilesPastTheWindowsPathLimit()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                Assert.Ignore("This regression applies to Windows file paths.");
            }
            string root = CreateScratch();
            try
            {
                string project = Path.Combine(root, "p");
                string staging = Path.Combine(
                    root,
                    "staging-" + new string('s', 48));
                string normalizedPath = "Assets/Product/" +
                    new string('f', 145) +
                    ".txt";
                string stagedPath = Path.Combine(
                    staging,
                    normalizedPath.Replace('/', Path.DirectorySeparatorChar));
                string livePath = Path.Combine(
                    project,
                    normalizedPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(project);
                Directory.CreateDirectory(
                    ExtendedWindowsPath(Path.GetDirectoryName(stagedPath)));
                File.WriteAllText(ExtendedWindowsPath(stagedPath), "verified");

                Assert.That(stagedPath.Length, Is.GreaterThanOrEqualTo(260));
                Assert.That(livePath.Length, Is.LessThan(260));

                ProjectTransactionResult result = ProjectTransactionJournal.Apply(
                    project,
                    staging,
                    "run-long-staging-path",
                    new[]
                    {
                        new VerifiedStagingFile
                        {
                            bytes = 8,
                            normalizedPath = normalizedPath,
                            sha256 = Sha256Text("verified"),
                        },
                    });

                Assert.That(result.state, Is.EqualTo("committed"));
                Assert.That(File.ReadAllText(livePath), Is.EqualTo("verified"));
            }
            finally
            {
                Directory.Delete(ExtendedWindowsPath(root), true);
            }
        }

        [Test]
        public void AssetEditingTransactionAlwaysEndsAfterFailure()
        {
            int beginCount = 0;
            int endCount = 0;

            Assert.Throws<InvalidOperationException>(() =>
                ProjectTransactionJournal.RunAssetEditingTransaction(
                    () => beginCount++,
                    () => throw new InvalidOperationException("failure"),
                    () => endCount++));

            Assert.AreEqual(1, beginCount);
            Assert.AreEqual(1, endCount);
        }

        private static VerifiedStagingFile Record(string path, string normalizedPath)
        {
            return new VerifiedStagingFile
            {
                bytes = new FileInfo(path).Length,
                normalizedPath = normalizedPath,
                sha256 = Sha256(path),
            };
        }

        private static string CreateScratch()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-transaction-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string ExtendedWindowsPath(string path)
        {
            return Path.DirectorySeparatorChar == '\\' &&
                !path.StartsWith(@"\\?\", StringComparison.Ordinal)
                ? @"\\?\" + Path.GetFullPath(path)
                : path;
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static string Sha256Text(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return BitConverter.ToString(
                        sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value)))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }
    }
}
