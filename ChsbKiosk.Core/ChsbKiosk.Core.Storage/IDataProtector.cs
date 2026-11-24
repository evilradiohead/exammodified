namespace ChsbKiosk.Core.Storage;

public interface IDataProtector
{
	byte[] Protect(byte[] plain);

	byte[] Unprotect(byte[] encrypted);
}
