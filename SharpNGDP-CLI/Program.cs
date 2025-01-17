﻿using SharpNGDP.Extensions;
using SharpNGDP.Files;
using SharpNGDP.Ribbit;
using SharpNGDP.TACT;
using SharpNGDP.Managers;
using System;
using System.IO;
using System.Linq;

namespace SharpNGDP
{
    class Program
    {
        static void Main(string[] args)
        {
            //printSummary();
            //printProducts();
            //downloadWoWConfigs();
            installProduct("hsb");

            readLoop();
        }

        private static void readLoop()
        {
            var ngdp = new NGDPClient();
            do
            {
                Console.WriteLine("Enter command:");
                var cmd = Console.ReadLine();
                if (string.IsNullOrEmpty(cmd)) break;

                var response = ngdp.FileManager.Get(new RibbitRequest(ngdp.Context, cmd));
                var file = response.GetFile<RibbitFile>();
                Console.WriteLine(file.MimeMessage.TextBody);
                Console.WriteLine();
                Console.WriteLine("Press ENTER to exit or any other key to continue");
            } while (Console.ReadKey().Key != ConsoleKey.Enter);
        }

        private static void printSummary()
        {
            var ngdp = new NGDPClient();
            var summary = ngdp.GetSummary();
            const string SUMMARY_ALIGN_FORMAT = "{0,-20} | {1, 8} | {2, -8}";
            var header = string.Format(SUMMARY_ALIGN_FORMAT, "Product", "Seq #", "Flags");
            Console.WriteLine(header);
            Console.WriteLine(new string('-', header.Length));
            foreach (var s in summary)
            {
                Console.WriteLine(SUMMARY_ALIGN_FORMAT,
                    s.Product, s.Seqn, s.Flags);
            }
            Console.WriteLine();
        }

        private static void printProducts()
        {
            var ngdp = new NGDPClient();
            // Only known WoW products
            var productNames = new string[] { "wow", "wow_beta", "wow_classic", "wow_classic_beta", "wowdev", "wowe1", "wowe2", "wowe3", "wowt", "wowv", "wowz", "hsb"};
            // Filter inactive products
            var summary = ngdp.GetSummary();
            var filteredProductNames = productNames.Where(p => summary.Any(s => s.Product == p && string.IsNullOrEmpty(s.Flags)));

            // Format string for alignment
            const string PRODUCT_ALIGN_FORMAT = "{0,-18} | {1, 4} | {2, 4} | {3, 15} | {4, 6}";

            var header = string.Format(PRODUCT_ALIGN_FORMAT, "Name", "#Ver", "#CDN", "Version", "Build");
            Console.WriteLine(header);
            Console.WriteLine(new string('-', header.Length));
            foreach (var productName in filteredProductNames)
            {
                var versions = ngdp.GetProductVersions(productName);
                var cdns = ngdp.GetProductCDNs(productName);
                var freshest = versions.OrderByDescending(v => v.BuildId).FirstOrDefault();

                Console.WriteLine(PRODUCT_ALIGN_FORMAT,
                    productName, versions.Count(), cdns.Count(), freshest?.VersionsName ?? "-", freshest?.BuildId ?? "-");
            }
            Console.WriteLine();
        }

        private static void downloadWoWConfigs()
        {
            var ngdp = new NGDPClient();

            var version = ngdp.GetProductVersions("wow").OrderByDescending(v => v.BuildId).FirstOrDefault();
            var cdn = ngdp.GetPreferredCDN(ngdp.GetProductCDNs("wow"));

            var buildConfig = ngdp.FileManager
                .Get(new TACTRequest(cdn, CDNRequestType.Config, version.BuildConfig))
                .GetFile<KeyValueFile>();
            Console.WriteLine($"{version.BuildConfig} BuildConfig");
            foreach (var kvp in buildConfig.Dictionary)
                Console.WriteLine("{0, 30} | {1}", kvp.Key, kvp.Value);
            Console.WriteLine();

            var cdnConfig = ngdp.FileManager
                .Get(new TACTRequest(cdn, CDNRequestType.Config, version.CDNConfig))
                .GetFile<KeyValueFile>();
            Console.WriteLine($"{version.CDNConfig} CDNConfig");
            foreach (var kvp in cdnConfig.Dictionary)
                Console.WriteLine("{0, 30} | {1}", kvp.Key, kvp.Value);
            Console.WriteLine();

            foreach (var archive in cdnConfig.Dictionary["archives"].Split(' '))
            {
                Console.WriteLine($"{archive}.index ArchiveIndexFile");
                var archiveIndex = ngdp.FileManager
                    .Get(new TACTRequest(cdn, CDNRequestType.Data, $"{archive}.index"))
                    .GetFile<ArchiveIndexFile>();
            }

            Console.WriteLine($"{buildConfig.Dictionary["install"].Split(' ')[1]} InstallFile");
            var installFile = ngdp.FileManager
                .Get(new TACTRequest(cdn, CDNRequestType.Data, buildConfig.Dictionary["install"].Split(' ')[1]))
                .GetFile<InstallFile>();

            Console.WriteLine($"{buildConfig.Dictionary["download"].Split(' ')[1]} DownloadFile");
            var downloadFile = ngdp.FileManager
                .Get(new TACTRequest(cdn, CDNRequestType.Data, buildConfig.Dictionary["download"].Split(' ')[1]))
                .GetFile<DownloadFile>();

            // This file is yuge
            Console.WriteLine($"{buildConfig.Dictionary["encoding"].Split(' ')[1]} EncodingFile");
            var encodingFile = ngdp.FileManager
                .Get(new TACTRequest(cdn, CDNRequestType.Data, buildConfig.Dictionary["encoding"].Split(' ')[1]))
                .GetFile<EncodingFile>();
        }

        private static void CopyToFile(Stream stream, string destpath)
        {
            using (var fs = new FileStream(destpath, FileMode.Create, FileAccess.Write))
                stream.CopyTo(fs);
        }

        private static void installProduct(string productName)
        {
            var ngdp = new NGDPClient();

            Console.WriteLine($"Looking up product '{productName}'");

            var versions = ngdp.GetProductVersions(productName);
            var version = versions.OrderByDescending(v => v.BuildId).FirstOrDefault();
            Console.WriteLine($"Found {versions.Count()} versions. Selecting build {version.BuildId}.");

            var cdns = ngdp.GetProductCDNs(productName);
            var cdn = ngdp.GetPreferredCDN(cdns);
            Console.WriteLine($"Found {cdns.Count()} CDNs. Selecting '{cdn.Name}'");

            var vmgr = new VersionManager(version, cdn);
            var amgr = new ArchiveManager(ngdp, cdn);
            amgr.AddArchives(vmgr.CDNConfig.Dictionary["archives"].Split(' '));

            var baseDir = version.BuildConfig;
            // Install files
            {
                var tag = vmgr.InstallFile.InstallFileTags.FirstOrDefault(t => t.Name.ToLower() == ngdp.Context.Platform.ToLower());
                Console.WriteLine($"InstallFile contains {vmgr.InstallFile.InstallFileTags.Length} tags. Selecting '{tag.Name}'");

                var files = vmgr.InstallFile.GetEntriesWithTag(tag);
                Console.WriteLine($"{files.Count()} entries tagged. Downloading...");

                foreach (var file in files)
                {
                    var ckey = file.Hash.ToHexString();
                    var ekey = vmgr.GetEKeyByCKey(ckey);
                    Console.WriteLine($"Downloading (CKey={ckey}, EKey={ekey}) as '{file.Name}' ({file.Size} bytes)...");

                    var fp = Path.Combine(baseDir, file.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(fp));
                    CopyToFile(vmgr.RequestFile(file.Hash.ToHexString()), fp);
                }
            }

            /*
            // Download files
            {
                var tag = vmgr.DownloadFile.DownloadTags.FirstOrDefault(t => t.Name.ToLower() == ngdp.Context.Platform.ToLower());
                Console.WriteLine($"DownloadFile contains {vmgr.DownloadFile.DownloadTags.Length} tags. Selecting '{tag.Name}'");

                var files = vmgr.DownloadFile.GetEntriesWithTag(tag);
                Console.WriteLine($"{files.Count()} entries tagged. Downloading...");

                foreach (var file in files.OrderBy(f => f.DownloadPriority))
                {
                    var ckey = file.Hash.ToHexString();
                    var ekey = vmgr.GetEKeyByCKey(ckey);
                    Console.WriteLine($"Downloading (CKey={ckey}, EKey={ekey}) as '{ckey}' ({file.Size} bytes)...");

                    var fp = Path.Combine(baseDir, "data", ckey);
                    Directory.CreateDirectory(Path.GetDirectoryName(fp));
                    CopyToFile(vmgr.RequestFile(file.Hash.ToHexString()), fp);
                }
            }
            */


            Console.WriteLine("Done!");
            Console.ReadKey();
        }
    }
}
