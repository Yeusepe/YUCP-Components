using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public class UpdateDeliveryServiceTests
    {
        private const string TestPackageId = "com.yucp.song";

        [SetUp]
        public void SetUp()
        {
            ResetState();
        }

        [TearDown]
        public void TearDown()
        {
            ResetState();
        }

        [Test]
        public void TryResolveAuthorizedInstallPlan_ParsesServerAuthorizedAliasInstallPlan()
        {
            PersistVerificationSession("verification:read products:read");

            using var server = new LocalHttpServer(HandleHappyPathRequest);
            var aliasPackage = new AliasPackageContract
            {
                kind = "alias-v1",
                aliasId = "song-thing",
                packageName = TestPackageId,
                installStrategy = UpdateDeliveryService.ServerAuthorizedInstallStrategy,
                importerPackage = "com.yucp.importer",
                catalogProductIds = new System.Collections.Generic.List<string> { "catalog_1" },
            };

            bool resolved = UpdateDeliveryService.TryResolveAuthorizedInstallPlan(
                server.BaseUrl,
                aliasPackage,
                out UpdateDeliveryService.AliasInstallPlan installPlan,
                out string error);

            Assert.That(resolved, Is.True, error);
            Assert.That(error, Is.Null.Or.Empty);
            Assert.That(installPlan, Is.Not.Null);
            Assert.That(installPlan.kind, Is.EqualTo(UpdateDeliveryService.AliasInstallPlanKind));
            Assert.That(installPlan.creatorRepoRef, Is.EqualTo("auth-user-1"));
            Assert.That(installPlan.productRef, Is.EqualTo("song-thing"));
            Assert.That(installPlan.repositoryUrl, Is.EqualTo(server.BaseUrl + "/v1/backstage/repos/auth-user-1/index.json"));
            Assert.That(installPlan.packages, Has.Length.EqualTo(1));
            Assert.That(installPlan.packages[0].packageId, Is.EqualTo(TestPackageId));
            Assert.That(installPlan.packages[0].importerDelivery.repoCatalogDeliveryMode, Is.EqualTo(UpdateDeliveryService.RepoTokenVpmDeliveryMode));
            Assert.That(server.CapturedAuthorizationHeader, Is.EqualTo("Bearer access-token"));
            Assert.That(server.ProductEndpointHits, Is.EqualTo(1));
            Assert.That(server.InstallPlanEndpointHits, Is.EqualTo(1));
        }

        [Test]
        public void TryResolveAuthorizedInstallPlan_RejectsUnexpectedImporterDeliveryMode()
        {
            PersistVerificationSession("verification:read products:read");

            using var server = new LocalHttpServer(HandleInvalidDeliveryModeRequest);
            var aliasPackage = new AliasPackageContract
            {
                kind = "alias-v1",
                aliasId = "song-thing",
                packageName = TestPackageId,
                installStrategy = UpdateDeliveryService.ServerAuthorizedInstallStrategy,
                importerPackage = "com.yucp.importer",
                catalogProductIds = new System.Collections.Generic.List<string> { "catalog_1" },
            };

            bool resolved = UpdateDeliveryService.TryResolveAuthorizedInstallPlan(
                server.BaseUrl,
                aliasPackage,
                out _,
                out string error);

            Assert.That(resolved, Is.False);
            Assert.That(error, Does.Contain("unsupported repo delivery mode"));
        }

        [Test]
        public void TryAuthorizePackage_RejectsServerAuthorizedAliasPackagesBeforeLegacyUnlock()
        {
            ProtectedAssetUnlockServiceTestHooks.InstalledPackageResolver = packageId =>
            {
                if (!string.Equals(packageId, TestPackageId, StringComparison.Ordinal))
                {
                    return null;
                }

                return new InstalledPackageInfo
                {
                    packageId = packageId,
                    aliasPackage = new AliasPackageContract
                    {
                        kind = "alias-v1",
                        aliasId = "song-thing",
                        packageName = packageId,
                        installStrategy = UpdateDeliveryService.ServerAuthorizedInstallStrategy,
                    },
                };
            };

            bool authorized = ProtectedAssetUnlockService.TryAuthorizePackage(
                TestPackageId,
                "protected-asset-1",
                out ProtectedAssetUnlockService.ProtectedAssetUnlockGrant grant,
                out string error);

            Assert.That(authorized, Is.False);
            Assert.That(grant, Is.Null);
            Assert.That(error, Does.Contain("server-authorized delivery"));
            Assert.That(error, Does.Contain("UpdateDeliveryService"));
        }

        [Test]
        public void BuildInstallRequest_SummarizesAliasInstallPlanPaths()
        {
            var metadata = new PackageMetadata("Creator Alias")
            {
                version = "1.2.3",
                aliasPackage = new AliasPackageContract
                {
                    kind = "alias-v1",
                    aliasId = "creator.alias",
                    packageName = "com.creator.alias",
                    packageDisplayName = "Creator Alias",
                    packageVersion = "1.2.3",
                    installStrategy = "server-authorized",
                    installPlan = new AliasInstallPlanMetadata
                    {
                        operation = "install",
                        managedPaths = new System.Collections.Generic.List<string>
                        {
                            "Packages/com.creator.alias/package.json",
                            "Packages/com.creator.alias/Runtime/Avatar.asset",
                        },
                        generatedPaths = new System.Collections.Generic.List<string>
                        {
                            ".yucp-dvi/Importer/InstallState/creator.alias.install-state.json",
                        },
                        sharedPaths = new System.Collections.Generic.List<string>
                        {
                            "Packages/packages-lock.json",
                        },
                    }
                }
            };

            AliasInstallPlanConfirmationService.ConfirmationRequest request =
                AliasInstallPlanConfirmationService.BuildInstallRequest(metadata);

            Assert.That(request, Is.Not.Null);
            Assert.That(request.title, Is.EqualTo("Confirm Alias Install"));
            Assert.That(request.confirmButton, Is.EqualTo("Install Package"));
            Assert.That(request.message, Does.Contain("Install alias package 'Creator Alias'?"));
            Assert.That(request.message, Does.Contain("Alias: creator.alias"));
            Assert.That(request.message, Does.Contain("Managed paths: 2"));
            Assert.That(request.message, Does.Contain("Generated paths: 1"));
            Assert.That(request.message, Does.Contain("Shared preserved paths: 1"));
            Assert.That(request.message, Does.Contain("Packages/com.creator.alias/package.json"));
            Assert.That(request.message, Does.Contain(".yucp-dvi/Importer/InstallState/creator.alias.install-state.json"));
        }

        [Test]
        public void BuildUpdateRequest_SummarizesResolvedPackagesAndPaths()
        {
            var packageInfo = new InstalledPackageInfo
            {
                packageId = TestPackageId,
                packageName = "Song Thing",
            };
            var installPlan = new UpdateDeliveryService.AliasInstallPlan
            {
                kind = UpdateDeliveryService.AliasInstallPlanKind,
                creatorName = "Mapache",
                creatorRepoRef = "auth-user-1",
                productRef = "song-thing",
                title = "Song Thing",
                repositoryUrl = "https://repo.example/index.json",
                expiresAt = 4102444800,
                packages = new[]
                {
                    new UpdateDeliveryService.AliasInstallPlanPackage
                    {
                        packageId = TestPackageId,
                        displayName = "Song Thing Package",
                        version = "1.2.3",
                        channel = "stable",
                        aliasContract = new AliasPackageContract
                        {
                            kind = "alias-v1",
                            aliasId = "song-thing",
                            installPlan = new AliasInstallPlanMetadata
                            {
                                managedPaths = new System.Collections.Generic.List<string>
                                {
                                    "Packages/com.yucp.song/package.json",
                                },
                                generatedPaths = new System.Collections.Generic.List<string>
                                {
                                    ".yucp-dvi/Importer/InstallState/song-thing.install-state.json",
                                },
                                sharedPaths = new System.Collections.Generic.List<string>
                                {
                                    "Packages/packages-lock.json",
                                },
                            }
                        },
                        importerDelivery = new UpdateDeliveryService.ImporterDeliveryContract
                        {
                            packageInstallStrategy = UpdateDeliveryService.ServerAuthorizedInstallStrategy,
                            repoCatalogDeliveryMode = UpdateDeliveryService.RepoTokenVpmDeliveryMode,
                            repoCatalogReadOnly = true,
                        }
                    }
                }
            };

            AliasInstallPlanConfirmationService.ConfirmationRequest request =
                AliasInstallPlanConfirmationService.BuildUpdateRequest(packageInfo, installPlan);

            Assert.That(request, Is.Not.Null);
            Assert.That(request.title, Is.EqualTo("Confirm Alias Update"));
            Assert.That(request.confirmButton, Is.EqualTo("Apply Update"));
            Assert.That(request.message, Does.Contain("Update alias package 'Song Thing'?"));
            Assert.That(request.message, Does.Contain("Creator: Mapache"));
            Assert.That(request.message, Does.Contain("Repository catalog: https://repo.example/index.json"));
            Assert.That(request.message, Does.Contain("Packages to download/apply: 1"));
            Assert.That(request.message, Does.Contain("Song Thing Package (com.yucp.song) v1.2.3 [stable]"));
            Assert.That(request.message, Does.Contain("Managed paths: 1"));
            Assert.That(request.message, Does.Contain(".yucp-dvi/Importer/InstallState/song-thing.install-state.json"));
        }

        [Test]
        public void TryApplyAuthorizedInstallPlan_AppliesAliasPlanAndRegistersInstalledPackage()
        {
            var installPlan = new UpdateDeliveryService.AliasInstallPlan
            {
                kind = UpdateDeliveryService.AliasInstallPlanKind,
                creatorName = "Mapache",
                creatorRepoRef = "auth-user-1",
                productRef = "song-thing",
                title = "Song Thing",
                repositoryUrl = "https://repo.example/index.json",
                packages = new[]
                {
                    new UpdateDeliveryService.AliasInstallPlanPackage
                    {
                        packageId = TestPackageId,
                        displayName = "Song Thing Package",
                        version = "1.2.3",
                        zipSha256 = new string('a', 64),
                        aliasContract = new AliasPackageContract
                        {
                            kind = "alias-v1",
                            aliasId = "song-thing",
                            packageName = TestPackageId,
                            packageDisplayName = "Song Thing",
                            packageVersion = "1.2.3",
                            installStrategy = UpdateDeliveryService.ServerAuthorizedInstallStrategy,
                            importerPackage = "com.yucp.importer",
                            installPlan = new AliasInstallPlanMetadata
                            {
                                managedPaths = new System.Collections.Generic.List<string>
                                {
                                    "Packages/com.yucp.song/package.json",
                                    "Packages/com.yucp.song/Embedded/Icons/song.png",
                                },
                                generatedPaths = new System.Collections.Generic.List<string>
                                {
                                    ".yucp-dvi/Importer/InstallState/song-thing.install-state.json",
                                },
                            }
                        },
                        importerDelivery = new UpdateDeliveryService.ImporterDeliveryContract
                        {
                            packageInstallStrategy = UpdateDeliveryService.ServerAuthorizedInstallStrategy,
                            repoCatalogDeliveryMode = UpdateDeliveryService.RepoTokenVpmDeliveryMode,
                            repoCatalogReadOnly = true,
                        }
                    }
                }
            };

            int applyCalls = 0;
            InstalledPackageInfo registeredPackage = null;
            InstalledPackageInfo persistedPackage = null;

            UpdateDeliveryServiceTestHooks.ApplyAuthorizedInstallPlanHandler = _ => applyCalls++;
            UpdateDeliveryServiceTestHooks.InstalledPackageMetadataLoader = packageId =>
            {
                Assert.That(packageId, Is.EqualTo(TestPackageId));
                return new PackageMetadata("Song Thing")
                {
                    version = "1.2.3",
                    description = "Alias metadata loaded from the installed package.",
                    aliasPackage = installPlan.packages[0].aliasContract.Clone(),
                    fileHashes = new System.Collections.Generic.List<PackageFileHashEntry>
                    {
                        new PackageFileHashEntry
                        {
                            path = "Packages/com.yucp.song/package.json",
                            hash = new string('b', 64),
                        }
                    }
                };
            };
            UpdateDeliveryServiceTestHooks.PersistInstallStateHandler = packageInfo =>
            {
                persistedPackage = packageInfo;
                packageInfo.installStateManifestPath = ".yucp-dvi/Importer/InstallState/song-thing.install-state.json";
            };
            UpdateDeliveryServiceTestHooks.RegisterInstalledPackageHandler = packageInfo =>
            {
                registeredPackage = packageInfo;
            };

            object[] args = { installPlan, null };
            MethodInfo method = typeof(UpdateDeliveryService).GetMethod(
                "TryApplyAuthorizedInstallPlan",
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(method, Is.Not.Null, "Expected TryApplyAuthorizedInstallPlan to exist.");

            bool applied = (bool)method.Invoke(null, args);
            string error = args[1] as string;

            Assert.That(applied, Is.True, error);
            Assert.That(error, Is.Null.Or.Empty);
            Assert.That(applyCalls, Is.EqualTo(1));
            Assert.That(persistedPackage, Is.Not.Null);
            Assert.That(registeredPackage, Is.Not.Null);
            Assert.That(registeredPackage.packageId, Is.EqualTo(TestPackageId));
            Assert.That(registeredPackage.installedVersion, Is.EqualTo("1.2.3"));
            Assert.That(registeredPackage.installedFiles, Has.Member("Packages/com.yucp.song/package.json"));
            Assert.That(registeredPackage.installStateManifestPath, Is.EqualTo(".yucp-dvi/Importer/InstallState/song-thing.install-state.json"));
        }

        [Test]
        public void TryApplyAuthorizedInstallPlan_FailsWhenInstalledMetadataCannotBeLoaded()
        {
            var installPlan = new UpdateDeliveryService.AliasInstallPlan
            {
                kind = UpdateDeliveryService.AliasInstallPlanKind,
                repositoryUrl = "https://repo.example/index.json",
                packages = new[]
                {
                    new UpdateDeliveryService.AliasInstallPlanPackage
                    {
                        packageId = TestPackageId,
                        version = "1.2.3",
                        aliasContract = new AliasPackageContract
                        {
                            kind = "alias-v1",
                            aliasId = "song-thing",
                            packageName = TestPackageId,
                            installStrategy = UpdateDeliveryService.ServerAuthorizedInstallStrategy,
                            importerPackage = "com.yucp.importer",
                        },
                        importerDelivery = new UpdateDeliveryService.ImporterDeliveryContract
                        {
                            packageInstallStrategy = UpdateDeliveryService.ServerAuthorizedInstallStrategy,
                            repoCatalogDeliveryMode = UpdateDeliveryService.RepoTokenVpmDeliveryMode,
                            repoCatalogReadOnly = true,
                        }
                    }
                }
            };

            UpdateDeliveryServiceTestHooks.ApplyAuthorizedInstallPlanHandler = _ => { };
            UpdateDeliveryServiceTestHooks.InstalledPackageMetadataLoader = _ => null;

            object[] args = { installPlan, null };
            MethodInfo method = typeof(UpdateDeliveryService).GetMethod(
                "TryApplyAuthorizedInstallPlan",
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(method, Is.Not.Null, "Expected TryApplyAuthorizedInstallPlan to exist.");

            bool applied = (bool)method.Invoke(null, args);
            string error = args[1] as string;

            Assert.That(applied, Is.False);
            Assert.That(error, Does.Contain("installed alias metadata"));
        }

        [Test]
        public void ConfirmUpdate_UsesInjectedDecisionHandler()
        {
            var packageInfo = new InstalledPackageInfo
            {
                packageId = TestPackageId,
                packageName = "Song Thing",
            };
            var installPlan = new UpdateDeliveryService.AliasInstallPlan
            {
                kind = UpdateDeliveryService.AliasInstallPlanKind,
                title = "Song Thing",
                repositoryUrl = "https://repo.example/index.json",
                packages = new[]
                {
                    new UpdateDeliveryService.AliasInstallPlanPackage
                    {
                        packageId = TestPackageId,
                        displayName = "Song Thing Package",
                    }
                }
            };

            AliasInstallPlanConfirmationService.ConfirmationRequest capturedRequest = null;
            bool confirmed = AliasInstallPlanConfirmationService.ConfirmUpdate(
                packageInfo,
                installPlan,
                request =>
                {
                    capturedRequest = request;
                    return false;
                });

            Assert.That(confirmed, Is.False);
            Assert.That(capturedRequest, Is.Not.Null);
            Assert.That(capturedRequest.title, Is.EqualTo("Confirm Alias Update"));
        }

        private static async Task HandleHappyPathRequest(HttpListenerContext context, LocalHttpServer server)
        {
            string path = context.Request.Url.AbsolutePath;
            server.CapturedAuthorizationHeader = context.Request.Headers["Authorization"];

            if (context.Request.HttpMethod == "GET" &&
                string.Equals(path, "/api/public/v2/products/catalog_1", StringComparison.OrdinalIgnoreCase))
            {
                server.ProductEndpointHits++;
                await server.WriteJsonAsync(
                    context,
                    "{\"authUserId\":\"auth-user-1\",\"providerProductRef\":\"song-thing\",\"canonicalSlug\":\"song-thing\"}");
                return;
            }

            if (context.Request.HttpMethod == "POST" &&
                string.Equals(path, "/api/backstage/access/auth-user-1/song-thing/install-plan", StringComparison.OrdinalIgnoreCase))
            {
                server.InstallPlanEndpointHits++;
                await server.WriteJsonAsync(
                    context,
                    "{"
                    + "\"kind\":\"alias-install-plan-v1\","
                    + "\"expiresAt\":4102444800,"
                    + "\"creatorName\":\"Mapache\","
                    + "\"creatorRepoRef\":\"auth-user-1\","
                    + "\"productRef\":\"song-thing\","
                    + "\"title\":\"Song Thing\","
                    + "\"repositoryUrl\":\"" + server.BaseUrl + "/v1/backstage/repos/auth-user-1/index.json\","
                    + "\"packages\":[{"
                    + "\"packageId\":\"" + TestPackageId + "\","
                    + "\"displayName\":\"Song Thing Package\","
                    + "\"version\":\"1.2.3\","
                    + "\"channel\":\"stable\","
                    + "\"zipSha256\":\"" + new string('a', 64) + "\","
                    + "\"aliasContract\":{"
                    + "\"kind\":\"alias-v1\","
                    + "\"aliasId\":\"song-thing\","
                    + "\"installStrategy\":\"server-authorized\","
                    + "\"importerPackage\":\"com.yucp.importer\","
                    + "\"catalogProductIds\":[\"catalog_1\"]"
                    + "},"
                    + "\"importerDelivery\":{"
                    + "\"packageInstallStrategy\":\"server-authorized\","
                    + "\"repoCatalogDeliveryMode\":\"repo-token-vpm-v1\","
                    + "\"repoCatalogReadOnly\":true"
                    + "}"
                    + "}]"
                    + "}");
                return;
            }

            context.Response.StatusCode = 404;
            context.Response.Close();
        }

        private static async Task HandleInvalidDeliveryModeRequest(HttpListenerContext context, LocalHttpServer server)
        {
            string path = context.Request.Url.AbsolutePath;
            server.CapturedAuthorizationHeader = context.Request.Headers["Authorization"];

            if (context.Request.HttpMethod == "GET" &&
                string.Equals(path, "/api/public/v2/products/catalog_1", StringComparison.OrdinalIgnoreCase))
            {
                await server.WriteJsonAsync(
                    context,
                    "{\"authUserId\":\"auth-user-1\",\"providerProductRef\":\"song-thing\",\"canonicalSlug\":\"song-thing\"}");
                return;
            }

            if (context.Request.HttpMethod == "POST" &&
                string.Equals(path, "/api/backstage/access/auth-user-1/song-thing/install-plan", StringComparison.OrdinalIgnoreCase))
            {
                await server.WriteJsonAsync(
                    context,
                    "{"
                    + "\"kind\":\"alias-install-plan-v1\","
                    + "\"repositoryUrl\":\"" + server.BaseUrl + "/v1/backstage/repos/auth-user-1/index.json\","
                    + "\"packages\":[{"
                    + "\"packageId\":\"" + TestPackageId + "\","
                    + "\"aliasContract\":{"
                    + "\"kind\":\"alias-v1\","
                    + "\"aliasId\":\"song-thing\","
                    + "\"installStrategy\":\"server-authorized\","
                    + "\"importerPackage\":\"com.yucp.importer\""
                    + "},"
                    + "\"importerDelivery\":{"
                    + "\"packageInstallStrategy\":\"server-authorized\","
                    + "\"repoCatalogDeliveryMode\":\"legacy-vpm\","
                    + "\"repoCatalogReadOnly\":true"
                    + "}"
                    + "}]"
                    + "}");
                return;
            }

            context.Response.StatusCode = 404;
            context.Response.Close();
        }

        private static void ResetState()
        {
            ProtectedAssetUnlockServiceTestHooks.Reset();
            UpdateDeliveryServiceTestHooks.Reset();
            VerificationIntentServiceTestHooks.Reset();
            InvokePrivateSignOutWithoutRegistry();
        }

        private static void PersistVerificationSession(string scope)
        {
            Type oauthType = typeof(CreatorIdentityOAuthService);
            Type sessionType = oauthType.GetNestedType("OAuthSessionV2", BindingFlags.NonPublic);
            Assert.That(sessionType, Is.Not.Null);

            object session = Activator.CreateInstance(sessionType, nonPublic: true);
            SetField(sessionType, session, "storageVersion", 2);
            SetField(sessionType, session, "accessToken", "access-token");
            SetField(sessionType, session, "accessTokenExpiresAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600);
            SetField(sessionType, session, "refreshToken", "refresh-token");
            SetField(sessionType, session, "refreshTokenExpiresAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 7200);
            SetField(sessionType, session, "userId", "auth-user-1");
            SetField(sessionType, session, "displayName", "Test User");
            SetField(sessionType, session, "scope", scope);

            MethodInfo persistSession = oauthType.GetMethod(
                "PersistSession",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(persistSession, Is.Not.Null);
            persistSession.Invoke(null, new object[] { session });
        }

        private static void InvokePrivateSignOutWithoutRegistry()
        {
            MethodInfo signOut = typeof(CreatorIdentityOAuthService).GetMethod(
                "SignOut",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(System.Collections.Generic.IReadOnlyList<InstalledPackageInfo>) },
                null);

            Assert.That(signOut, Is.Not.Null);
            signOut.Invoke(null, new object[] { new System.Collections.Generic.List<InstalledPackageInfo>() });
        }

        private static void SetField(Type type, object instance, string fieldName, object value)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}' on '{type.FullName}'.");
            field.SetValue(instance, value);
        }

        private sealed class LocalHttpServer : IDisposable
        {
            private readonly HttpListener _listener;
            private readonly Func<HttpListenerContext, LocalHttpServer, Task> _handler;
            private readonly Task _listenTask;

            internal LocalHttpServer(Func<HttpListenerContext, LocalHttpServer, Task> handler)
            {
                _handler = handler;

                int port = FindFreePort();
                BaseUrl = $"http://127.0.0.1:{port}";
                _listener = new HttpListener();
                _listener.Prefixes.Add(BaseUrl + "/");
                _listener.Start();
                _listenTask = Task.Run(ListenAsync);
            }

            internal string BaseUrl { get; }
            internal string CapturedAuthorizationHeader { get; set; }
            internal int ProductEndpointHits { get; set; }
            internal int InstallPlanEndpointHits { get; set; }

            public void Dispose()
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch
                {
                }

                try
                {
                    _listenTask.Wait(TimeSpan.FromSeconds(1));
                }
                catch
                {
                }
            }

            internal async Task WriteJsonAsync(HttpListenerContext context, string json)
            {
                byte[] body = Encoding.UTF8.GetBytes(json);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body, 0, body.Length);
                context.Response.Close();
            }

            private async Task ListenAsync()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync();
                    }
                    catch
                    {
                        break;
                    }

                    _ = Task.Run(() => _handler(context, this));
                }
            }

            private static int FindFreePort()
            {
                var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                return port;
            }
        }
    }
}
