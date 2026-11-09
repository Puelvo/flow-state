namespace FlowState.Mobile;

/// <summary>Android: veri uygulamaya özel (diğer uygulamaların okuyamadığı) klasörde durur; ek şifreleme yok.</summary>
public sealed class AndroidSecretCodec : ISecretCodec
{
	public string Protect(string plain) => plain;
	public string Unprotect(string stored) => stored;
}
