using System;
using System.IO;
using System.Text;
using IntegrateDrv.Utilities.FileSystem;
using IntegrateDrv.Utilities.PortableExecutable;
using IntegrateDrv.WindowsDirectory;
using Microsoft.Deployment.Compression.Cab;

namespace IntegrateDrv.Integrators
{
	public class KernelAndHalIntegrator
	{
		private readonly WindowsInstallation _installation;
		private static bool _uniprocessorKernelEnabled;
		private static bool _multiprocessorHalEnabled;

		public KernelAndHalIntegrator(WindowsInstallation installation)
		{
			_installation = installation;
		}

		// The following combinations of kernel and HAL will enable NICs to work in text-mode setup:
		// (regardless of whether the machine being used has one or more than one CPU core)
		// 
		// Uniprocessor kernel   + ACPI PC HAL
		// Uniprocessor kernel   + ACPI Uniprocessor PC HAL
		// Multiprocessor kernel + ACPI Multiprocessor PC HAL
		//
		// Other combinations will either hang or reboot.
		// By default, text-mode setup will use Multiprocessor kernel + ACPI Uniprocessor PC HAL (which will hang)
		//
		// Note that we can use both multiprocessor kernel and multiprocessor HAL on uniprocessor machine,
		// (there might be a performance penalty for such configuration)
		// however, this approach will not work on older machines that uses the ACPI PC HAL (Pentium 3 / VirtualBox)
		// so the best approach is using the uniprocessor kernel.

		// references:
		// http://support.microsoft.com/kb/309283
		// http://social.technet.microsoft.com/Forums/en-AU/configmgrosd/thread/fb1dbea9-9d39-4663-9c61-6bcdb4c1253f

		// general information about x86 HAL types: http://www.vernalex.com/guides/sysprep/hal.shtml

		// Note: /kernel switch does not work in text-mode, so we can't use this simple solution.
		public void UseUniprocessorKernel()
		{
			if (_uniprocessorKernelEnabled)
				return;

			// amd64 and presumably ia64 use a single HAL for both uni and multiprocessor kernel)
			if (_installation.ArchitectureIdentifier != "x86")
				return;

			Console.WriteLine();
			Console.WriteLine("By default, text-mode setup will use a multiprocessor OS kernel with a");
			Console.WriteLine("uniprocessor HAL. This configuration cannot support network adapters");
			Console.WriteLine("(setup will hang).");
			Console.WriteLine("IntegrateDrv will try to enable uniprocessor kernel:");

			string setupldrPath;
			if (_installation.IsTargetContainsTemporaryInstallation)
			{
				// sometimes NTLDR is being used instead of $LDR$ (when using winnt32.exe /syspart /tempdrive)
				// (even so, it's still a copy of setupldr.bin and not NTLDR from \I386)
				setupldrPath = _installation.TargetDirectory + "$LDR$";
				if (!File.Exists(setupldrPath))
					setupldrPath = _installation.TargetDirectory + "NTLDR";
			}
			else // integration to installation media
				setupldrPath = _installation.SetupDirectory + "setupldr.bin";

			if (!File.Exists(setupldrPath))
			{
				Console.WriteLine("Error: '{0}' does not exist.", setupldrPath);
				Program.Exit();
			}

			if (_installation.IsWindows2000)
				PatchWindows2000SetupBootLoader(setupldrPath);
			else
				// Winndows XP or Windows Server 2003
				PatchWindowsXP2003SetupBootLoader(setupldrPath);

			if (_installation.IsTargetContainsTemporaryInstallation)
				ProgramUtils.CopyCriticalFile(
					_installation.SetupDirectory + "ntoskrnl.ex_",
					_installation.BootDirectory + "ntoskrnl.ex_");
			else
				// integration to installation media
				_installation.DOSNetInf.InstructSetupToCopyFileFromSetupDirectoryToBootDirectory("ntoskrnl.ex_");
			Console.WriteLine("Uniprocessor kernel has been enabled.");

			_uniprocessorKernelEnabled = true;
		}

		private static void PatchWindows2000SetupBootLoader(string setupldrPath)
		{
			// Windows 2000 boot loader does not contain a portable executable or a checksum verification mechanism
			var bytes = File.ReadAllBytes(setupldrPath);
			// update executable byte array
			var oldSequence = Encoding.ASCII.GetBytes("ntkrnlmp.exe");
			var newSequence = Encoding.ASCII.GetBytes("ntoskrnl.exe");
			ReplaceInBytes(ref bytes, oldSequence, newSequence);
			FileSystemUtils.ClearReadOnlyAttribute(setupldrPath);
			File.WriteAllBytes(setupldrPath, bytes);
		}

		private void PatchWindowsXP2003SetupBootLoader(string setupldrPath)
		{
			var bytes = File.ReadAllBytes(setupldrPath);
			var dosSignature = BitConverter.GetBytes(DOSHeader.DOSSignature);

			// setupldr.bin and ntldr are regular executables that are preceded by a special loader
			// we use the MZ DOS signature to determine where the executable start.
			// Note that we must update the executable checksum, because the loader will verify that the executable checksum is correct
			var executableOffset = IndexOfSequence(ref bytes, dosSignature);

			if (executableOffset == -1)
			{
				Console.WriteLine("Error: setupldr.bin is corrupted.");
				Program.Exit();
			}

			var loader = new byte[executableOffset];
			Array.Copy(bytes, loader, executableOffset);

			var executableLength = bytes.Length - executableOffset;
			var executable = new byte[executableLength];
			Array.Copy(bytes, executableOffset, executable, 0, executableLength);

			var peInfo = new PortableExecutableInfo(executable);

			// update executable byte array
			var oldSequence = Encoding.ASCII.GetBytes("ntkrnlmp.exe");
			var newSequence = Encoding.ASCII.GetBytes("ntoskrnl.exe");
			// the kernel filename appears in the first PE section
			var section = peInfo.Sections[0];
			ReplaceInBytes(ref section, oldSequence, newSequence);
			peInfo.Sections[0] = section;

			var peStream = new MemoryStream();
			PortableExecutableInfo.WritePortableExecutable(peInfo, peStream);
			executable = peStream.ToArray();
			// finished updating executable byte array

			FileSystemUtils.ClearReadOnlyAttribute(setupldrPath);
			var stream = new FileStream(setupldrPath, FileMode.Create, FileAccess.ReadWrite);
			var writer = new BinaryWriter(stream);
			writer.Write(loader);
			writer.Write(executable);
			writer.Close();
		}

		/// <returns>true if replacement occured</returns>
		public static bool ReplaceInBytes(ref byte[] bytes, byte[] oldSequence, byte[] newSequence)
		{
			var result = false;
			if (oldSequence.Length != newSequence.Length)
				throw new ArgumentException("oldSequence must have the same length as newSequence");

			var index = IndexOfSequence(ref bytes, oldSequence);
			while (index != -1)
			{
				result = true;
				for (var j = 0; j < newSequence.Length; j++)
					bytes[index + j] = newSequence[j];
				index = IndexOfSequence(ref bytes, oldSequence);
			}
			return result;
		}

		private static int IndexOfSequence(ref byte[] bytes, byte[] sequence)
		{
			for (var index = 0; index < bytes.Length - sequence.Length; index++)
			{
				var match = true;
				for (var j = 0; j < sequence.Length; j++)
				{
					if (bytes[index + j] != sequence[j])
					{
						match = false;
						break;
					}
				}
				if (match)
					return index;
			}
			return -1;
		}

		// This approach does not work for older machines that uses the ACPI PC HAL (Pentium 3 / VirtualBox)
		[Obsolete]
		public void UseMultiprocessorHal()
		{
			if (_multiprocessorHalEnabled)
				return;
			if (_installation.ArchitectureIdentifier != "x86")
				// amd64 and presumably ia64 use a single HAL for both uni and multiprocessor kernel)
				return;

			Console.WriteLine();
			Console.WriteLine("By default, text-mode setup will use a multiprocessor OS kernel");
			Console.WriteLine("with a uniprocessor HAL. This configuration cannot support network adapters");
			Console.WriteLine("(setup will hang).");
			Console.WriteLine("IntegrateDrv will try to enable multiprocessor HAL:");

			if (_installation.IsTargetContainsTemporaryInstallation)
			{
				if (File.Exists(_installation.BootDirectory + "halmacpi.dl_"))
				{
					_installation.TextSetupInf.UseMultiprocessorHal();
					_multiprocessorHalEnabled = true;
					Console.WriteLine("Multiprocessor HAL has been enabled.");
				}
				else if (File.Exists(_installation.SetupDirectory + "halmacpi.dl_"))
				{
					ProgramUtils.CopyCriticalFile(_installation.SetupDirectory + "halmacpi.dl_", _installation.BootDirectory + "halmacpi.dl_");
					_installation.TextSetupInf.UseMultiprocessorHal();
					Console.WriteLine("halmacpi.dl_ was copied from local source directory.");
					_multiprocessorHalEnabled = true;
					Console.WriteLine("Multiprocessor HAL has been enabled.");
				}
				else
				{
					int index;
					for(index = 3; index >= 1; index--)
					{
						var spFilename = string.Format("sp{0}.cab", index);
						if (File.Exists(_installation.SetupDirectory + spFilename))
						{
							var cabInfo = new CabInfo(_installation.SetupDirectory + spFilename);
							if (cabInfo.GetFile("halmacpi.dll") != null)
							{
								cabInfo.UnpackFile("halmacpi.dll", _installation.BootDirectory + "halmacpi.dll");
								// setup is expecting a packed "halmacpi.dl_"
								//cabInfo = new CabInfo(m_installation.BootDirectory + "halmacpi.dl_");
								//Dictionary<string, string> files = new Dictionary<string, string>();
								//files.Add("halmacpi.dll", "halmacpi.dll");
								//cabInfo.PackFileSet(m_installation.BootDirectory, files);
								Console.WriteLine("halmacpi.dl_ was extracted from local source directory.");
								_installation.TextSetupInf.UseMultiprocessorHal();
								_multiprocessorHalEnabled = true;
								Console.WriteLine("Multiprocessor HAL has been enabled.");
							}
							break;
						}
					}

					if (index == 0)
						Console.WriteLine("Warning: could not locate halmacpi.dll, multiprocessor HAL has not been enabled!");
				}
			}
			else // integration to installation media
			{
				_installation.TextSetupInf.UseMultiprocessorHal();
				_installation.DOSNetInf.InstructSetupToCopyFileFromSetupDirectoryToBootDirectory("halmacpi.dl_");
				_multiprocessorHalEnabled = true;
				Console.WriteLine("Multiprocessor HAL has been enabled.");
			}
		}
	}
}
