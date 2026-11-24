using System.Security.Cryptography;
using System.Text;
using ChsbKiosk.Core.Storage;

namespace ChsbKiosk.Security;

public sealed class DpapiProtector : IDataProtector
{
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CHSB-Plus/local-state/v1");

	public byte[] Protect(byte[] plain)
	{
		return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
	}

	public byte[] Unprotect(byte[] encrypted)
	{
		return ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
	}
}
