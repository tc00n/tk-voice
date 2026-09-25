namespace TKVoice.Core.Abstractions;

public interface ICredentialService
{
    string? GetOpenAIApiKey();
    void SetOpenAIApiKey(string apiKey);
}
