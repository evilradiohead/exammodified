using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ChsbKiosk.Core.Api;

namespace ChsbKiosk.Core.Update;

public static class UpdateVerifier
{
	public const string PublicKeyPem = "-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEyHaCHa9O/7Z4+iqEcRJOyrXrRiEZ\n6tD32M+KaHtryL0skEm7EYysoCKjnmJ16gk3hVIi8ATG1qTvSQl07/U1og==\n-----END PUBLIC KEY-----";

	// ILSpy source-generator chiqishini kod sifatida tikladi; uni oddiy keshlangan Regex bilan
	// almashtirdik (chaqiruvchi joylar o'zgarmadi). System.Text.RegularExpressions.Generated papkasi olib tashlandi.
	private static readonly Regex _fileNamePattern = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,120}\\.exe$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex _versionPattern = new Regex("^[0-9]{1,3}(\\.[0-9]{1,4}){1,3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static Regex FileNamePattern()
	{
		return _fileNamePattern;
	}

	private static Regex VersionPattern()
	{
		return _versionPattern;
	}

	public static bool IsValidVersion(string? version)
	{
		if (version != null)
		{
			return VersionPattern().IsMatch(version);
		}
		return false;
	}

	public static bool IsValidFileName(string? name)
	{
		if (name != null)
		{
			return FileNamePattern().IsMatch(name);
		}
		return false;
	}

	public static bool IsNewer(string? candidate, string current)
	{
		if (!IsValidVersion(candidate) || !IsValidVersion(current))
		{
			return false;
		}
		int[] array = candidate.Split('.').Select(int.Parse).ToArray();
		int[] array2 = current.Split('.').Select(int.Parse).ToArray();
		for (int i = 0; i < Math.Max(array.Length, array2.Length); i++)
		{
			int num = ((i < array.Length) ? array[i] : 0);
			int num2 = ((i < array2.Length) ? array2[i] : 0);
			if (num != num2)
			{
				return num > num2;
			}
		}
		return false;
	}

	public static UpdatePackage? Select(UpdateInfo? info, string arch)
	{
		if (info?.Packages == null)
		{
			return null;
		}
		UpdatePackage updatePackage = null;
		foreach (UpdatePackage package in info.Packages)
		{
			if (package != null && string.Equals(package.Arch, arch, StringComparison.Ordinal) && IsWellFormed(package))
			{
				if (string.Equals(package.Kind, "update", StringComparison.Ordinal))
				{
					return package;
				}
				if (string.Equals(package.Kind, "full", StringComparison.Ordinal) && updatePackage == null)
				{
					updatePackage = package;
				}
			}
		}
		return updatePackage;
	}

	public static bool IsWellFormed(UpdatePackage package)
	{
		if (IsValidFileName(package.File))
		{
			long size = package.Size;
			if (size > 0 && size < 2000000000)
			{
				string sha = package.Sha256;
				if (sha != null && sha.Length == 64 && package.Sha256.All(delegate(char c)
				{
					switch (c)
					{
					case '0':
					case '1':
					case '2':
					case '3':
					case '4':
					case '5':
					case '6':
					case '7':
					case '8':
					case '9':
					case 'a':
					case 'b':
					case 'c':
					case 'd':
					case 'e':
					case 'f':
						return true;
					default:
						return false;
					}
				}))
				{
					return !string.IsNullOrWhiteSpace(package.Signature);
				}
			}
		}
		return false;
	}

	public static string SignedMessage(string version, UpdatePackage package)
	{
		return $"chsb-update|{version}|{package.Arch}|{package.Kind}|{package.Sha256}|{package.Size}";
	}

	public static bool VerifySignature(string version, UpdatePackage package, string? publicKeyPem = null)
	{
		if (!IsValidVersion(version) || !IsWellFormed(package))
		{
			return false;
		}
		try
		{
			byte[] signature = Convert.FromBase64String(package.Signature);
			using ECDsa eCDsa = ECDsa.Create();
			eCDsa.ImportFromPem((publicKeyPem ?? "-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEyHaCHa9O/7Z4+iqEcRJOyrXrRiEZ\n6tD32M+KaHtryL0skEm7EYysoCKjnmJ16gk3hVIi8ATG1qTvSQl07/U1og==\n-----END PUBLIC KEY-----").AsSpan());
			return eCDsa.VerifyData(Encoding.UTF8.GetBytes(SignedMessage(version, package)), signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
		}
		catch (Exception ex) when (((ex is FormatException || ex is CryptographicException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			return false;
		}
	}

	public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default(CancellationToken))
	{
		string result;
		await using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, useAsync: true))
		{
			result = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(continueOnCapturedContext: false)).ToLowerInvariant();
		}
		return result;
	}

	public static async Task<bool> VerifyFileAsync(string path, string version, UpdatePackage package, CancellationToken ct = default(CancellationToken), string? publicKeyPem = null)
	{
		if (!VerifySignature(version, package, publicKeyPem) || !File.Exists(path))
		{
			return false;
		}
		if (new FileInfo(path).Length != package.Size)
		{
			return false;
		}
		string s = await ComputeSha256Async(path, ct).ConfigureAwait(continueOnCapturedContext: false);
		return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s), Encoding.ASCII.GetBytes(package.Sha256));
	}
}
