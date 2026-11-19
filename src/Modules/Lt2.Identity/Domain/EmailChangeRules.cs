namespace Lt2.Identity.Domain;

/// <summary>What a session must show before the account's address may change (design 6.2, "Proving it is them first").</summary>
internal enum EmailChangeProof
{
    /// <summary>The session is a passkey session: nothing more.</summary>
    Passkey,

    /// <summary>A code sent to the address in use, typed in the same form.</summary>
    Code,

    /// <summary>A code session without the code: a passkey session or the code to the address in use is needed.</summary>
    ProofRequired,

    /// <summary>A staff account outside a session signed in with its staff passkey.</summary>
    StaffPasskeyRequired,

    /// <summary>A staff-passkey session whose passkey was not confirmed in the last ten minutes.</summary>
    ConfirmationRequired,

    /// <summary>The MP's own account (design 8.5.4) outside a passkey session.</summary>
    PasskeyRequired,
}

/// <summary>
/// The rules of changing the account's address (design 6.2). A stolen session must not be enough to move an account: the change
/// starts only in a passkey session or after a code to the address in use; a staff account needs a staff-passkey session with a
/// fresh confirmation, and the MP's own account a passkey session, as for everything it does. The code to the new address follows
/// <see cref="SignInCodeRules"/>; the old address's link back is good for <see cref="RestoreLifetime"/>.
/// </summary>
internal static class EmailChangeRules
{
    /// <summary>How long the link to the old address puts it back.</summary>
    public static readonly TimeSpan RestoreLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// Which proof the session gives, or why it gives none. <paramref name="staffAccount"/>: the account has an activated staff
    /// role; <paramref name="staffSession"/>: this session was signed in with its staff passkey; <paramref name="fresh"/>: that
    /// passkey was confirmed in the last ten minutes; <paramref name="mpAccount"/>: the MP's own linked account;
    /// <paramref name="passkeySession"/>: signed in with a passkey still on the account; <paramref name="codeGiven"/>: a code to
    /// the address in use came with the request. A staff account and the MP's account never prove it by a code.
    /// </summary>
    public static EmailChangeProof Proof(bool staffAccount, bool staffSession, bool fresh, bool mpAccount, bool passkeySession, bool codeGiven)
    {
        if (staffAccount)
        {
            return !staffSession ? EmailChangeProof.StaffPasskeyRequired
                : fresh ? EmailChangeProof.Passkey
                : EmailChangeProof.ConfirmationRequired;
        }

        if (passkeySession)
        {
            return EmailChangeProof.Passkey;
        }

        if (mpAccount)
        {
            return EmailChangeProof.PasskeyRequired;
        }

        return codeGiven ? EmailChangeProof.Code : EmailChangeProof.ProofRequired;
    }
}
