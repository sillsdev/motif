namespace SIL.Motif.App.Services;

/// <summary>Stores one assistant credential in the operating system's credential store.</summary>
public interface ICredentialStore
{
    /// <summary>Returns the stored credential, or null when none is available.</summary>
    string? Read();

    /// <summary>Saves the credential and returns whether it was persisted.</summary>
    bool Write(string credential);

    /// <summary>Removes the stored credential if the store is available.</summary>
    void Delete();
}
