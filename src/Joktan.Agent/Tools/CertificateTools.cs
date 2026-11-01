using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Certificates;
using Joktan.Certificates.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class CertificateTools
{
    private static readonly ToolParameter CertificateName = P.Text("certificate", "The certificate's name, from certificates_list.");

    private static readonly ToolParameter[] SubjectFields =
    [
        P.Text("name", "Its name in the app."), P.OptText("common_name", "Its common name."), P.OptText("organization", "Its organisation."), P.OptText("organizational_unit", "Its unit."),
        P.OptText("locality", "Its locality."), P.OptText("state", "Its state."), P.OptText("country", "Its two-letter country."), P.OptText("email", "Its email address."),
        P.OptText("alternative_names", "Other names and addresses, for a server or client."), P.OptChoice("key_algorithm", "The kind of key.", Enum.GetNames<KeyAlgorithm>()),
        P.OptText("valid_until", "The last day it is valid, as yyyy-MM-dd."), P.OptText("folder", "Where its files go."),
    ];

    public static IEnumerable<McpTool> Create(CertificatesViewModel page) =>
    [
        Read("certificates_list", "List the certificates as their chains, with validity and problems.", [], _ => page.Roots.Select(Describe).ToList()),
        Read("certificates_get", "Read a certificate's details. Private keys are not given.", [CertificateName], args =>
        {
            var node = Find(page, args);
            return new
            {
                node.Name, node.KindText, node.IssuerName, node.UsedBy, node.Subject, Issuer = node.IssuerSubject, node.ValidFrom, node.ValidUntil, node.StatusText, node.Problem,
                node.KeyText, node.AlternativeNames, node.Usage, node.SerialNumber, node.Fingerprint, node.Folder, node.CertificatePath, node.KeyPath, node.ChainPath, node.AuthorityPath,
            };
        }),
        ChangeAsync("certificates_new_root", "Make a root CA.", SubjectFields, args => CreateAsync(page, args, () => page.NewAuthorityCommand.ExecuteAsync(null))),
        ChangeAsync("certificates_issue", "Make a certificate signed by a CA.",
            [P.Text("issuer", "The name of the CA to sign it."), P.Choice("kind", "What it is for.", Enum.GetNames<CertificateKind>()), .. SubjectFields], args =>
            {
                page.Selected = FindByName(page, args.Text("issuer"));
                if (!page.IssueCommand.CanExecute(null))
                    throw new ToolFailure($"{args.Text("issuer")} is not a CA.");
                var kind = Enum.Parse<CertificateKind>(args.Text("kind"), ignoreCase: true);
                return CreateAsync(page, args, () => page.IssueCommand.ExecuteAsync(kind));
            }),
        RiskyAsync("certificates_export", "Write a certificate's files to a folder. With include_key, its private key goes too.",
            [
                CertificateName, P.Text("folder", "The folder to write to."), P.OptText("base_name", "What the files are called."), P.OptFlag("include_key", "Include the private key."),
                P.OptFlag("include_authority", "Include the CA's certificate."), P.OptFlag("include_chain", "Include the chain."), P.OptFlag("include_pkcs12", "Include a PKCS#12 file."),
                P.OptText("pkcs12_password", "The PKCS#12 file's password."), P.OptFlag("replace_existing", "Replace files already there."),
            ], async args =>
            {
                page.Selected = Find(page, args);
                var previous = page.EditExport;
                IReadOnlyList<string> written = [];
                string? problem = null;
                page.EditExport = export =>
                {
                    export.Folder = args.Text("folder");
                    if (args.OptText("base_name") is { } baseName) export.BaseName = baseName;
                    if (args.Flag("include_key") is { } key) export.IncludeKey = key;
                    if (args.Flag("include_authority") is { } authority) export.IncludeAuthority = authority;
                    if (args.Flag("include_chain") is { } chain) export.IncludeChain = chain;
                    if (args.Flag("include_pkcs12") is { } pkcs12) export.IncludePkcs12 = pkcs12;
                    if (args.OptText("pkcs12_password") is { } password) export.Pkcs12Password = password;
                    if (args.Flag("replace_existing") is { } replace) export.ReplaceExisting = replace;

                    var saved = false;
                    export.CloseRequested += ok => saved = ok;
                    if (export.ExportCommand.CanExecute(null))
                        export.ExportCommand.Execute(null);
                    written = export.Written;
                    if (!saved)
                        problem = export.Error ?? "It cannot be exported with those choices.";
                    return Task.FromResult(saved);
                };
                try
                {
                    if (!page.ExportCommand.CanExecute(null))
                        throw new ToolFailure("It cannot be exported: its certificate could not be read.");
                    await page.ExportCommand.ExecuteAsync(null);
                }
                finally
                {
                    page.EditExport = previous;
                }

                Check(problem);
                return new { Written = written };
            }),
        Change("certificates_rename", "Rename a certificate.", [CertificateName, P.Text("name", "Its new name.")], args =>
        {
            page.Selected = Find(page, args);
            page.RenameText = args.Text("name");
            page.RenameCommand.Execute(null);
            Check(page.Error);
            return "Renamed.";
        }),
        Risky("certificates_delete", "Delete a certificate, its key and those it signed. This cannot be undone.", [CertificateName], args =>
        {
            page.Selected = Find(page, args);
            var what = page.DeleteText;
            page.ConfirmDeleteCommand.Execute(null);
            Check(page.Error);
            return $"Done: {what}";
        }),
        Read("certificates_defaults", "Read the defaults for new certificates.", [], _ =>
        {
            var d = page.Defaults;
            return new
            {
                d.ClientFolder, d.RootCommonName, d.IntermediateCommonName, d.ServerCommonName, d.ServerAlternativeNames, d.ClientCommonName, d.Organization, d.OrganizationalUnit,
                d.Locality, d.State, d.Country, KeyAlgorithm = d.SelectedKeyAlgorithm?.Value.ToString(), d.AuthorityValidityDays, d.CertificateValidityDays,
            };
        }),
        Change("certificates_defaults_update", "Change the defaults for new certificates. Give the property names as certificates_defaults does.", [P.Map("values", "The names and new values.")], args =>
        {
            var d = page.Defaults;
            foreach (var (name, value) in args.Map("values"))
            {
                switch (name)
                {
                    case nameof(d.ClientFolder): d.ClientFolder = value; break;
                    case nameof(d.RootCommonName): d.RootCommonName = value; break;
                    case nameof(d.IntermediateCommonName): d.IntermediateCommonName = value; break;
                    case nameof(d.ServerCommonName): d.ServerCommonName = value; break;
                    case nameof(d.ServerAlternativeNames): d.ServerAlternativeNames = value; break;
                    case nameof(d.ClientCommonName): d.ClientCommonName = value; break;
                    case nameof(d.Organization): d.Organization = value; break;
                    case nameof(d.OrganizationalUnit): d.OrganizationalUnit = value; break;
                    case nameof(d.Locality): d.Locality = value; break;
                    case nameof(d.State): d.State = value; break;
                    case nameof(d.Country): d.Country = value; break;
                    case nameof(d.AuthorityValidityDays): d.AuthorityValidityDays = value; break;
                    case nameof(d.CertificateValidityDays): d.CertificateValidityDays = value; break;
                    case "KeyAlgorithm":
                        d.SelectedKeyAlgorithm = d.KeyAlgorithms.FirstOrDefault(c => c.Value.ToString() == value) ?? throw new ToolFailure($"No key algorithm is called {value}.");
                        break;
                    default:
                        throw new ToolFailure($"There is no default called {name}.");
                }
            }

            Check(d.Error);
            return "Saved.";
        }),
    ];

    private static async Task<object?> CreateAsync(CertificatesViewModel page, ToolArgs args, Func<Task> run)
    {
        var previous = page.EditNewCertificate;
        string? problem = null;
        string? made = null;
        page.EditNewCertificate = editor =>
        {
            editor.Name = args.Text("name");
            if (args.OptText("common_name") is { } commonName) editor.CommonName = commonName;
            if (args.OptText("organization") is { } organization) editor.Organization = organization;
            if (args.OptText("organizational_unit") is { } unit) editor.OrganizationalUnit = unit;
            if (args.OptText("locality") is { } locality) editor.Locality = locality;
            if (args.OptText("state") is { } state) editor.State = state;
            if (args.OptText("country") is { } country) editor.Country = country;
            if (args.OptText("email") is { } email) editor.Email = email;
            if (args.OptText("alternative_names") is { } names) editor.AlternativeNames = names;
            if (args.OptText("folder") is { } folder) editor.Folder = folder;
            if (args.OptText("key_algorithm") is { } algorithm)
                editor.SelectedKeyAlgorithm = editor.KeyAlgorithms.FirstOrDefault(c => c.Value.ToString() == algorithm) ?? throw new ToolFailure($"No key algorithm is called {algorithm}.");
            if (args.OptText("valid_until") is { } until)
                editor.ValidUntil = DateTime.ParseExact(until, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            var saved = false;
            editor.CloseRequested += ok => saved = ok;
            if (editor.SaveCommand.CanExecute(null))
                editor.SaveCommand.Execute(null);
            if (!saved)
                problem = editor.Error ?? "The details are not complete.";
            made = editor.Created?.DisplayName;
            return Task.FromResult(saved);
        };
        try
        {
            await run();
        }
        finally
        {
            page.EditNewCertificate = previous;
        }

        Check(problem);
        return new { Made = made };
    }

    private static object Describe(CertificateNodeViewModel node) => new
    {
        node.Name, node.KindText, node.ValidUntil, node.StatusText, node.CanIssue, node.UsedBy, Children = node.Children.Select(Describe).ToList(),
    };

    private static CertificateNodeViewModel Find(CertificatesViewModel page, ToolArgs args) => FindByName(page, args.Text("certificate"));

    private static CertificateNodeViewModel FindByName(CertificatesViewModel page, string name)
    {
        var found = page.Roots.SelectMany(r => r.SelfAndDescendants()).Where(n => n.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return found.Count switch
        {
            0 => throw new ToolFailure($"No certificate is called {name}."),
            1 => found[0],
            _ => throw new ToolFailure($"{found.Count} certificates are called {name}. Rename one on the Certificates page first."),
        };
    }
}
