using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using GitDependencies;
using System.Diagnostics;

namespace GitDepsPacker
{
	struct Parameters
	{
		// Specifies the path of the package root
		public string RootPath;
		// Output file name
		public string Target;
		// Directory for created UEPACK files
		public string Storage;
		// Base url of storage directory to gitdeps.xml file
		public string BaseUrl;
		// Remove path
		public string RemotePath;
		// Ignore proxy flag to gitdeps.xml file
		public bool IgnoreProxy;
		// Ignore git-tracked files
		public bool IgnoreGit;
		// Directory/file with ingnored gitdeps.xml files
		public List<String> Ignore;
		// Directory/file with patched gitdeps.xml files
		public List<String> Patch;
		// Directory/file with already created gitdeps.xml file for UEPACK reuse
		public List<String> Reuse;
		// Optimal UEPACK size in bytes
		public int Optimal;
		// Workder thread count.
		public int Threads;
		// Wildcards.
		public List<Wildcard> Wildcards;
	}

	class Program
	{
		private static byte[] Signature = Encoding.UTF8.GetBytes("UEPACK00");

		static int Main(string[] Args)
		{
			Parameters Params;
			// Build the argument list. Remove any double-hyphens from the start of arguments for conformity with other Epic tools.
			Params.Wildcards = new List<Wildcard>();
			List<string> ArgsList = new List<string>();
			foreach (string Arg in Args)
			{
				if (Arg.StartsWith("-"))
				{
					ArgsList.Add(Arg.StartsWith("--") ? Arg.Substring(1) : Arg);
				}
				else
				{
					Params.Wildcards.Add(new Wildcard(Arg));
				}
			}

			// Parse the parameters
			Params.RootPath = ParseParameter(ArgsList, "-root=", Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "../../..")));
			Params.Target = ParseParameter(ArgsList, "-target=", Path.GetFullPath(Path.Combine(Params.RootPath, "../uepacks.gitdeps.xml")));
			Params.Storage = ParseParameter(ArgsList, "-storage=", Path.GetFullPath(Path.Combine(Params.RootPath, "../uepacks/")));
			Params.BaseUrl = ParseParameter(ArgsList, "-base-url=", null);
			Params.RemotePath = ParseParameter(ArgsList, "-remote-path=", null);
			Params.IgnoreProxy = ParseSwitch(ArgsList, "-ignore-proxy");
			Params.IgnoreGit = ParseSwitch(ArgsList, "-ignore-git");
			Params.Ignore = ParseFiles(ArgsList, "-ignore=", "*.gitdeps.xml", null);
			Params.Patch = ParseFiles(ArgsList, "-patch=", "*.gitdeps.xml", Path.GetFullPath(Path.Combine(Params.RootPath, "Engine/Build")));
			Params.Reuse = ParseFiles(ArgsList, "-reuse=", "*.gitdeps.xml", null);
			Params.Threads = Math.Max(int.Parse(ParseParameter(ArgsList, "-threads=", "4")), 1);
			Params.Optimal = int.Parse(ParseParameter(ArgsList, "-optimal=", "10")) * 1024 * 1024;

			// If there are any more parameters, print an error
			bool bHelp = ParseSwitch(ArgsList, "-help");
			foreach (string RemainingArg in ArgsList)
			{
				Log.WriteLine("Invalid command line parameter: {0}", RemainingArg);
				Log.WriteLine();
				bHelp = true;
			}

			// Print the help message
			if (bHelp)
			{
				Log.WriteLine("Usage:");
				Log.WriteLine("   GitDepsPacker [options] [wilcards]");
				Log.WriteLine();
				Log.WriteLine("Options:");
				Log.WriteLine("   --root=<PATH>                 Specifies the path of the package root");
				Log.WriteLine("   --target=<X.gitdeps.xml>      Output file name");
				Log.WriteLine("   --storage=<PATH>              Directory for created UEPACK files");
				Log.WriteLine("   --base-url=<URL>              Add base url of storage directory to *.gitdeps.xml file");
				Log.WriteLine("   --remote-path=<PATH>          Remote path of created packs");
				Log.WriteLine("   --ignore-proxy                Add ignore proxy flag to *.gitdeps.xml file");
				Log.WriteLine("   --ignore=<PATH>               Directory/file with already created gitdeps.xml file for ignore file list");
				Log.WriteLine("   --patch=<PATH>                Directory/file with already created gitdeps.xml file for patching for remove changed file list");
				Log.WriteLine("   --reuse=<PATH>                Directory/file with already created gitdeps.xml file for UEPACK reuse");
				Log.WriteLine("   --optimal=<MB SIZE>           Optimal UEPACK size");
				Log.WriteLine("   --threads=X                   Use X threads");
				Log.WriteLine("   --ignore-git                  Ignore files, tracked by git");
				Log.WriteLine();
				Log.WriteLine("Wilcards example:");
				Log.WriteLine("   **/Binaries/                  Include all files in all subdirectories named Binaries");
				Log.WriteLine("   Engine/Intermediate/Build/Win64/Inc/                  Include all files in Engine/Intermediate/Build/Win64/Inc directory");
				Log.WriteLine("   Engine/Intermediate/Build/Win64/UE4Editor/**/*.lib    Include all *.lib files in Engine/Intermediate/Build/Win64/UE4Editor directory");
				Log.WriteLine("   !**/*.pdb                     Exclude all *.pdb files in all subdirectories");
				return 1;
			}

			Log.WriteLine("Current options:");
			Log.WriteLine("  Content root: {0}", Params.RootPath);
			Log.WriteLine("  Target .gitdeps.xml file: {0}", Params.Target);
			Log.WriteLine("  Output UEPACK storage path: {0}", Params.Storage);
			Log.WriteLine("  CDN base url: {0}", Params.BaseUrl == null ? "default" : Params.BaseUrl);
			Log.WriteLine("  Remote path: {0}", Params.RemotePath == null ? "none" : Params.RemotePath);
			Log.WriteLine("  Ignore proxy flag: {0}", Params.IgnoreProxy);
			LogFiles("  Patch already packed file list in:", Params.Patch);
			LogFiles("  Ignore already packed files from:", Params.Ignore);
			LogFiles("  Reuse UEPACK files from:", Params.Reuse);
			Log.WriteLine("  Ignore: already packed files from:" + (Params.Ignore.Count == 0 ? " none" : ""));
			foreach (string Item in Params.Ignore)
			{
				Log.WriteLine("  - {0}", Item);
			}
			Log.WriteLine("  Optimal pack size: {0} MB", Params.Optimal / (1024 * 1024));
			Log.WriteLine("  Worker threads: {0}", Params.Threads);
			Log.WriteLine();

			// Register a delegate to clear the status text if we use ctrl-c to quit
			Console.CancelKeyPress += delegate { Log.FlushStatus(); };
			return DoWork(Params);
		}

		private static void LogFiles(string Message, ICollection<string> Files)
		{
			Log.WriteLine(Message + (Files.Count == 0 ? " none" : ""));
			foreach (string Item in Files)
			{
				Log.WriteLine("  - {0}", Item);
			}
		}

		private static int DoWork(Parameters Params)
		{
			// Find all target files.
			Log.WriteLine("Search files...");
			ISet<string> TargetFiles = new HashSet<string>();
			FindAllFiles(TargetFiles, Path.GetFullPath(Params.RootPath), "", Params.Wildcards);
			// Remove Git files.
			if (Params.IgnoreGit)
			{
				Log.WriteLine("Remove git-tracked files...");
				TargetFiles = RemoveGitFiles(Path.GetFullPath(Params.RootPath), TargetFiles);
			}
			// Remove ignored files.
			Log.WriteLine("Remove ignored files...");
			foreach (string Item in Params.Ignore)
			{
				Log.WriteLine("  " + Item);
				RemoveIgnoreFiles(Item, TargetFiles);
			}
			// Calcuate blob information.
			Log.WriteLine("Calculate blob information...");
			ConcurrentDictionary<string, DependencyFile> DepFiles = new ConcurrentDictionary<string, DependencyFile>(); // path -> file
			ConcurrentDictionary<string, DependencyBlob> DepBlobs = new ConcurrentDictionary<string, DependencyBlob>(); // hash -> blob
			GenerateBlobList(Params.Threads, Params.RootPath, TargetFiles, DepFiles, DepBlobs);
			// Patch exists manifest files.
			Log.WriteLine("Patch exists manifest files...");
			foreach (string Item in Params.Patch)
			{
				Log.WriteLine("  " + Item);
				RemoveRepackedFiles(Item, DepFiles);
			}
			// Add blob reuse information.
			Log.WriteLine("Reuse manifest pack files...");
			ConcurrentDictionary<string, DependencyPack> DepPacks = new ConcurrentDictionary<string, DependencyPack>(); // hash -> pack
			foreach (string Item in Params.Reuse)
			{
				Log.WriteLine("  " + Item);
				UpdateReusedPacks(Item, DepBlobs, DepPacks);
			}
			// Generate pack files.
			Log.WriteLine("Generate pack files...");
			GeneratePackFiles(Params, DepFiles, DepBlobs, DepPacks);
			// Write manifest file
			Log.WriteLine("Write manifest file");
			GenerateManifest(Params, DepFiles, DepBlobs, DepPacks);
			return 0;
		}

		private static void GenerateBlobList(int ThreadCount, string Root, ISet<string> Files, ConcurrentDictionary<string, DependencyFile> DepFiles, ConcurrentDictionary<string, DependencyBlob> DepBlobs)
		{
			// Initialize StatFileHelper in main thread for workaroung mono non-theadsafe assembly loading.
			StatFileHelper.IsExecutalbe(".");

			ConcurrentQueue<string> FilesQueue = new ConcurrentQueue<string>(Files);
			Thread[] Threads = new Thread[ThreadCount];
			for (int i = 0; i < Threads.Length; ++i)
			{
				Threads[i] = new Thread(x => GenerateBlobListThread(Root, FilesQueue, DepFiles, DepBlobs));
				Threads[i].Start();
			}
			foreach (Thread T in Threads)
			{
				T.Join();
			}
		}

		private static void GenerateBlobListThread(string Root, ConcurrentQueue<string> FilesQueue, ConcurrentDictionary<string, DependencyFile> DepFiles, ConcurrentDictionary<string, DependencyBlob> DepBlobs)
		{
			string PackFile;
			while (FilesQueue.TryDequeue(out PackFile))
			{
				string FullPath = Path.Combine(Root, PackFile);
				long FileSize;
				// Add File info.
				DependencyFile DepFile = new DependencyFile();
				DepFile.IsExecutable = StatFileHelper.IsExecutalbe(FullPath);
				DepFile.Name = PackFile;
				DepFile.Hash = ComputeHashForFile(FullPath, out FileSize);
				if (DepFiles.TryAdd(PackFile, DepFile))
				{
					// Add Blob info.
					DependencyBlob DepBlob = new DependencyBlob();
					DepBlob.Hash = DepFile.Hash;
					DepBlob.Size = FileSize;
					DepBlobs.TryAdd(DepBlob.Hash, DepBlob);
				}
			}
		}

		public static string ComputeHashForFile(string FileName, out long FileSize)
		{
			using (FileStream InputStream = File.OpenRead(FileName))
			{
				byte[] Hash = new SHA1CryptoServiceProvider().ComputeHash(InputStream);
				FileSize = InputStream.Position;
				return BitConverter.ToString(Hash).ToLower().Replace("-", "");
			}
		}

		public static void ReadXmlObject<T>(string FileName, out T NewObject)
		{
			XmlSerializer Serializer = new XmlSerializer(typeof(T));
			using (StreamReader Reader = new StreamReader(FileName))
			{
				NewObject = (T)Serializer.Deserialize(Reader);
			}
		}
		public static void WriteXmlObject<T>(string FileName, T XmlObject)
		{
			XmlSerializer Serializer = new XmlSerializer(typeof(T));
			using (StreamWriter Writer = new StreamWriter(FileName))
			{
				Serializer.Serialize(Writer, XmlObject);
			}
		}

		private static void GenerateManifest(Parameters Params, IDictionary<string, DependencyFile> DepFiles, IDictionary<string, DependencyBlob> DepBlobs, IDictionary<string, DependencyPack> DepPacks)
		{
			DependencyManifest Manifest = new DependencyManifest();
			IDictionary<string, DependencyFile> Files = new SortedDictionary<string, DependencyFile>();
			IDictionary<string, DependencyBlob> Blobs = new SortedDictionary<string, DependencyBlob>();
			IDictionary<string, DependencyPack> Packs = new SortedDictionary<string, DependencyPack>();
			foreach (DependencyFile DepFile in DepFiles.Values)
			{
				Files.Add(DepFile.Name, DepFile);
				DependencyBlob DepBlob = DepBlobs[DepFile.Hash];
				if (!Blobs.ContainsKey(DepBlob.Hash))
				{
					Blobs.Add(DepBlob.Hash, DepBlob);
					DependencyPack DepPack = DepPacks[DepBlob.PackHash];
					if (!Packs.ContainsKey(DepPack.Hash))
					{
						Packs.Add(DepPack.Hash, DepPack);
					}
				}
			}

			Manifest.BaseUrl = Params.BaseUrl;
			Manifest.IgnoreProxy = Params.IgnoreProxy;
			Manifest.Files = Files.Values.ToArray();
			Manifest.Blobs = Blobs.Values.ToArray();
			Manifest.Packs = Packs.Values.ToArray();
			WriteXmlObject(Params.Target, Manifest);
		}

		private static void GeneratePackFiles(Parameters Params, IDictionary<string, DependencyFile> DepFiles, IDictionary<string, DependencyBlob> DepBlobs, ConcurrentDictionary<string, DependencyPack> DepPacks)
		{
			// Collect mapping from hash to file.
			IDictionary<string, string> BlobToFile = new Dictionary<string, string>();
			foreach (DependencyFile DepFile in DepFiles.Values)
			{
				if (!BlobToFile.ContainsKey(DepFile.Hash))
				{
					BlobToFile.Add(DepFile.Hash, Path.Combine(Params.RootPath, DepFile.Name));
				}
			}
			// Collect unpacked blobs.
			List<DependencyBlob> UnpackedBlobs = new List<DependencyBlob>();
			foreach (DependencyBlob DepBlob in DepBlobs.Values)
			{
				if ((DepBlob.PackHash == null) && BlobToFile.ContainsKey(DepBlob.Hash))
				{
					UnpackedBlobs.Add(DepBlob);
				}
			}
			// Sort by size (bigger first).
			UnpackedBlobs.Sort(CompareBlobsBySize);
			// Create pack files.
			if (!Directory.Exists(Params.Storage))
			{
				Directory.CreateDirectory(Params.Storage);
			}
			// Pack in some threads.
			ConcurrentQueue<DependencyBlob> BlobsQueue = new ConcurrentQueue<DependencyBlob>(UnpackedBlobs);
			Thread[] Threads = new Thread[Params.Threads];
			for (int i = 0; i < Threads.Length; ++i)
			{
				Threads[i] = new Thread(x => GeneratePackFilesThread(Params, BlobsQueue, BlobToFile, DepPacks));
				Threads[i].Start();
			}
			foreach (Thread T in Threads)
			{
				T.Join();
			}
		}

		private static void GeneratePackFilesThread(Parameters Params, ConcurrentQueue<DependencyBlob> BlobsQueue, IDictionary<string, string> BlobToFile, ConcurrentDictionary<string, DependencyPack> DepPacks)
		{
			while (true)
			{
				DependencyBlob DepBlob;
				if (!BlobsQueue.TryDequeue(out DepBlob))
				{
					return;
				}
				String TempPath = Path.Combine(Params.Storage, "." + Guid.NewGuid().ToString() + ".tmp");
				try
				{
					List<DependencyBlob> PackedBlobs = new List<DependencyBlob>();
					DependencyPack DepPack = new DependencyPack();
					using (FileStream Stream = File.Create(TempPath))
					{
						SHA1Managed Hasher = new SHA1Managed();
						using (GZipStream GZip = new GZipStream(Stream, CompressionMode.Compress, true))
						{
							CryptoStream HashStream = new CryptoStream(GZip, Hasher, CryptoStreamMode.Write);
							Stream PackStream = new WriteStream(HashStream);
							PackStream.Write(Signature, 0, Signature.Length);
							while (true)
							{
								PackedBlobs.Add(DepBlob);
								DepBlob.PackOffset = PackStream.Position;
								using (FileStream BlobFile = File.Open(BlobToFile[DepBlob.Hash], FileMode.Open, FileAccess.Read, FileShare.Read))
								{
									BlobFile.CopyTo(PackStream);
								}
								if ((Stream.Position > Params.Optimal) || (!BlobsQueue.TryDequeue(out DepBlob)))
								{
									break;
								}
							}
							DepPack.Size = PackStream.Position;
							HashStream.FlushFinalBlock();
						}
						DepPack.Hash = BitConverter.ToString(Hasher.Hash).ToLower().Replace("-", "");
						DepPack.CompressedSize = Stream.Position;
					}
					string PackFile = Path.Combine(Params.Storage, DepPack.Hash);
					if (!File.Exists(PackFile))
					{
						File.Move(TempPath, PackFile);
					}
					foreach (DependencyBlob Blob in PackedBlobs)
					{
						Blob.PackHash = DepPack.Hash;
					}
					DepPack.RemotePath = Params.RemotePath;
					DepPacks.TryAdd(DepPack.Hash, DepPack);
				}
				finally
				{
					if (File.Exists(TempPath))
					{
						File.Delete(TempPath);
					}
				}
			}
		}

		private static int CompareBlobsBySize(DependencyBlob A, DependencyBlob B)
		{
			return (A.Size == B.Size) ? A.Hash.CompareTo(B.Hash) : B.Size.CompareTo(A.Size);
		}

		private static void UpdateReusedPacks(string ManifestFile, IDictionary<string, DependencyBlob> DepBlobs, IDictionary<string, DependencyPack> DepPacks)
		{
			DependencyManifest Manifest;
			ReadXmlObject(ManifestFile, out Manifest);
			ISet<string> Packs = new HashSet<string>();
			foreach (DependencyBlob Item in Manifest.Blobs)
			{
				DependencyBlob DepBlob;
				if (DepBlobs.TryGetValue(Item.Hash, out DepBlob) && (DepBlob.PackHash == null))
				{
					DepBlobs[Item.Hash] = Item;
					Packs.Add(Item.PackHash);
				}
			}
			foreach (DependencyPack Item in Manifest.Packs)
			{
				if (Packs.Remove(Item.Hash))
				{
					if (!DepPacks.ContainsKey(Item.Hash))
					{
						DepPacks.Add(Item.Hash, Item);
					}
				}
			}
			if (Packs.Count != 0)
			{
				throw new InvalidDataException("Found broken manifest file: " + ManifestFile);
			}
		}

		private static void RemoveIgnoreFiles(string ManifestFile, ISet<string> TargetFiles)
		{
			DependencyManifest Manifest;
			ReadXmlObject(ManifestFile, out Manifest);
			foreach (DependencyFile Item in Manifest.Files)
			{
				TargetFiles.Remove(Item.Name);
			}
		}

		private static string FindExeFromPath(string ExeName, string ExpectedPathSubstring = null)
		{
			if (File.Exists(ExeName))
			{
				return Path.GetFullPath(ExeName);
			}

			foreach (string BasePath in Environment.GetEnvironmentVariable("PATH").Split(Path.PathSeparator))
			{
				var FullPath = Path.Combine(BasePath, ExeName);
				if (ExpectedPathSubstring == null || FullPath.IndexOf(ExpectedPathSubstring, StringComparison.InvariantCultureIgnoreCase) != -1)
				{
					if (File.Exists(FullPath))
					{
						return FullPath;
					}
				}
			}

			return null;
		}

		private static ISet<string> RemoveGitFiles(string RootPath, ISet<string> FoundFiles)
		{
#if __MonoCS__
			string git = FindExeFromPath("git");
#else
			string git = FindExeFromPath("git.exe");
#endif
			if (git == null) 
			{
				throw new FileNotFoundException("Can't get find git executable");
			}
			ProcessStartInfo start = new ProcessStartInfo();
			start.Arguments = "status --untracked-files=all --short --ignored .";
			start.FileName = git;
			start.WorkingDirectory = RootPath;
			start.UseShellExecute = false;
			start.WindowStyle = ProcessWindowStyle.Hidden;
			start.CreateNoWindow = true;
			start.RedirectStandardOutput = true;
		
			// Run the external process & wait for it to finish
			ISet<string> TargetFiles = new HashSet<string>();
			using (Process proc = Process.Start(start))
			{
				while (true)
				{
					string line = proc.StandardOutput.ReadLine();
					if (line == null) break;
					if ((line.StartsWith("!") || line.StartsWith("?")) && line.Length > 3)
					{
						string path = line.Substring(3);
						if (FoundFiles.Contains(path))
						{
							TargetFiles.Add(path);
						}
					}
				}

				proc.WaitForExit();

				// Retrieve the app's exit code
				int exitCode = proc.ExitCode;
				if (exitCode != 0)
				{
					throw new Exception("Can't get git unracked files list");
				}
			}
			return TargetFiles;
		}

		private static void RemoveRepackedFiles(string ManifestFile, IDictionary<string, DependencyFile> DepFiles)
		{
			DependencyManifest Manifest;
			ReadXmlObject(ManifestFile, out Manifest);

			List<DependencyFile> NewFiles = new List<DependencyFile>();
			foreach (DependencyFile Item in Manifest.Files)
			{
				DependencyFile DepFile;
				if (DepFiles.TryGetValue(Item.Name, out DepFile))
				{
					if (DepFile.Hash == Item.Hash)
					{
						// File not changed - do not repack.
						DepFiles.Remove(Item.Name);
						NewFiles.Add(Item);
					}
				}
				else
				{
					NewFiles.Add(Item);
				}
			}
			if (Manifest.Files.Length != NewFiles.Count)
			{
				Manifest.Files = NewFiles.ToArray();
				WriteXmlObject(ManifestFile, Manifest);
			}
		}

		static void FindAllFiles(ISet<string> TargetFiles, string RootPath, string SubPath, ICollection<Wildcard> Wildcards)
		{
			foreach (string FullName in Directory.EnumerateFiles(Path.Combine(RootPath, SubPath)))
			{
				string FileName = Path.GetFileName(FullName);
				string PackName = SubPath.Length > 0 ? SubPath + '/' + FileName : FileName;
				if (IsMatchWildcards(PackName, Wildcards, true))
				{
					TargetFiles.Add(PackName);
				}
			}
			foreach (string FullName in Directory.EnumerateDirectories(Path.Combine(RootPath, SubPath)))
			{
				string FileName = Path.GetFileName(FullName);
				string PackName = SubPath.Length > 0 ? SubPath + '/' + FileName : FileName;
				if (IsMatchWildcards(PackName, Wildcards, false))
				{
					FindAllFiles(TargetFiles, RootPath, PackName, Wildcards);
				}
			}
		}

		static bool IsMatchWildcards(string PackName, ICollection<Wildcard> Wildcards, bool FilePath)
		{
			if (Wildcards.Count == 0) return true;
			bool Found = false;
			foreach (Wildcard Item in Wildcards)
			{
				if ((Found == Item.Exclude) && Item.IsMatched(PackName, FilePath))
				{
					Found = !Found;
				}
			}
			return Found;
		}

		static List<string> ParseFiles(List<string> ArgsList, string Name, string DefaultMask, string DefaultPath)
		{
			List<string> Params = new List<string>(ParseParameters(ArgsList, Name));
			if (Params.Count() == 0 && DefaultPath != null)
			{
				Params.Add(DefaultPath);
			}
			List<string> Result = new List<string>();
			foreach (string Item in Params)
			{
				if (Item.Length == 0)
				{
				}
				else if (Directory.Exists(Item))
				{
					Result.AddRange(FindFiles(Item, DefaultMask));
				}
				else if (File.Exists(Item))
				{
					Result.Add(Item);
				}
				else
				{
					Result.AddRange(FindFiles(Path.GetDirectoryName(Item), Path.GetFileName(Item)));
				}
			}
			return Result;
		}

		static List<string> FindFiles(string Dir, string Mask)
		{
			List<string> Result = new List<string>();
			foreach (string Item in Directory.EnumerateFiles(Dir.Length > 0 ? Dir : ".", Mask, SearchOption.TopDirectoryOnly))
			{
				Result.Add(Item);
			}
			return Result;
		}

		static bool ParseSwitch(List<string> ArgsList, string Name)
		{
			for (int Idx = 0; Idx < ArgsList.Count; Idx++)
			{
				if (String.Compare(ArgsList[Idx], Name, true) == 0)
				{
					ArgsList.RemoveAt(Idx);
					return true;
				}
			}
			return false;
		}

		static string ParseParameter(List<string> ArgsList, string Prefix, string Default)
		{
			string Value = Default;
			for (int Idx = 0; Idx < ArgsList.Count; Idx++)
			{
				if (ArgsList[Idx].StartsWith(Prefix, StringComparison.CurrentCultureIgnoreCase))
				{
					Value = ArgsList[Idx].Substring(Prefix.Length);
					ArgsList.RemoveAt(Idx);
					break;
				}
			}
			return Value;
		}

		static IEnumerable<string> ParseParameters(List<string> ArgsList, string Prefix)
		{
			for (; ; )
			{
				string Value = ParseParameter(ArgsList, Prefix, null);
				if (Value == null)
				{
					break;
				}
				yield return Value;
			}
		}
	}
}
