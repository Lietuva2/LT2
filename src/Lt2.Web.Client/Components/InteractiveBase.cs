using FluentValidation.Results;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Lt2.Web.Client.Components;

/// <summary>
/// What every interactive component shares (design 6.4, <c>AGENTS.md</c> "islands"): one action at a time, controls
/// disabled until WebAssembly has taken over the prerendered markup (anything typed before would be lost), a message for
/// every failure, field errors under their fields, and whether the person is signed in. <typeparamref name="TText"/> is
/// the marker of the resource file the component's texts are in.
/// </summary>
public abstract class InteractiveBase<TText> : ComponentBase
{
    [Inject]
    protected IStringLocalizer<TText> L { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    /// <summary>For the one refusal that leads elsewhere: <c>terms.required</c> (design 8.22.2).</summary>
    [Inject]
    private NavigationManager TermsNavigation { get; set; } = default!;

    protected bool Busy { get; private set; }

    protected string? Message { get; set; }

    protected string? Info { get; set; }

    protected IReadOnlyDictionary<string, string[]> Errors { get; set; } = NoErrors();

    protected bool Locked => Busy || !RendererInfo.IsInteractive;

    /// <summary>Whether the page is rendered for a signed-in session (the server still checks every call).</summary>
    protected async Task<bool> IsSignedInAsync() => AuthState is not null && (await AuthState).User.Identity?.IsAuthenticated == true;

    protected static IReadOnlyDictionary<string, string[]> NoErrors() => new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs one action with the controls locked; a network, reading or browser failure becomes the message.</summary>
    protected async Task RunAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ClearMessages();
        Busy = true;
        try
        {
            await action();
        }
        catch (HttpRequestException)
        {
            Message = L["Error.Network"];
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException or JSException)
        {
            Message = L["Error.Generic"];
        }
        finally
        {
            Busy = false;
        }
    }

    protected void ClearMessages()
    {
        Message = null;
        Info = null;
        Errors = NoErrors();
    }

    /// <summary>True when the call succeeded; otherwise shows its problem (see <see cref="Show"/>) and returns false.</summary>
    protected bool Succeeded(ApiResult result, params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            return true;
        }

        Show(result.Problem, fields);
        return false;
    }

    /// <summary>
    /// Shows a failed answer: errors about the given fields under those fields, every other error in the note, so no
    /// refusal is ever silent; without field errors, the problem's detail or a general message.
    /// </summary>
    protected void Show(ApiProblem? problem, params string[] fields)
    {
        // Design 8.22.2: a member who has not accepted the terms in force is shown the acceptance screen, which brings them
        // back here, instead of each island failing its writes one by one. The message stays in case the browser stays.
        if (problem?.Code == "terms.required")
        {
            Message = problem.Detail ?? L["Error.Generic"];
            // Without the "#" part: a page's secret there (a token, design 6.8) must not go into the acceptance link's query.
            var here = "/" + TermsNavigation.ToBaseRelativePath(TermsNavigation.Uri).Split('#')[0];
            TermsNavigation.NavigateTo(TermsText.AcceptanceLink(here), forceLoad: true);
            return;
        }

        if (problem?.Errors is { Count: > 0 } errors)
        {
            Errors = errors.Where(e => fields.Contains(e.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
            var unseen = errors.Where(e => !fields.Contains(e.Key, StringComparer.OrdinalIgnoreCase)).SelectMany(e => e.Value).Distinct().ToList();
            if (unseen.Count > 0)
            {
                Message = string.Join(" ", unseen);
            }

            return;
        }

        Message = problem?.Detail ?? L["Error.Generic"];
    }

    /// <summary>The errors the shared validator found before sending, under their fields (camelCase, as the server names them).</summary>
    protected bool Valid(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsValid)
        {
            return true;
        }

        Errors = result.Errors
            .GroupBy(e => char.ToLowerInvariant(e.PropertyName[0]) + e.PropertyName[1..], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray(), StringComparer.OrdinalIgnoreCase);
        return false;
    }

    /// <summary>One error for one field, set by the form itself.</summary>
    protected void FieldFails(string field, string message) =>
        Errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [field] = [message] };

    /// <summary><c>field--error</c> while the field has an error.</summary>
    protected string ErrorClass(string field) => Errors.ContainsKey(field) ? "field--error" : "";

    /// <summary><c>aria-invalid</c> while the field has an error, none otherwise.</summary>
    protected string? Invalid(string field) => Errors.ContainsKey(field) ? "true" : null;

    /// <summary>
    /// <c>aria-describedby</c> of an input: its hints, and its error line (<see cref="FieldError"/> with <c>For</c> = the
    /// input's id) while there is one; none when there is nothing to point at.
    /// </summary>
    protected string? DescribedBy(string field, string inputId, params string[] hints)
    {
        var ids = hints.Where(h => !string.IsNullOrEmpty(h)).ToList();
        if (Errors.ContainsKey(field))
        {
            ids.Add(FieldError.IdFor(inputId));
        }

        return ids.Count == 0 ? null : string.Join(" ", ids);
    }
}
