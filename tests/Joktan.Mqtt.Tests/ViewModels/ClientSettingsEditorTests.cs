using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Joktan.Certificates;
using Joktan.Mqtt.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

public class ClientSettingsEditorTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static ClientSettingsViewModel Editor(ClientProfile? profile = null, IReadOnlyList<string>? others = null, bool isNew = false, CertificateStore? store = null, string password = "", string keyPassword = "") =>
        new(profile ?? new ClientProfile { Name = "one", Host = "h", ClientId = "id" }, password, keyPassword, others ?? [], isNew, store);

    private CertificateStore Store() => CertificateStore.Load(_folder.At("store"));

    private static StoredCertificate Make(CertificateStore store, string name, CertificateKind kind, StoredCertificate? issuer = null) =>
        store.Create(new CertificateSpec { Kind = kind, Name = name, CommonName = name, IssuerId = issuer?.Id, NotAfter = DateTimeOffset.UtcNow.AddYears(1) });

    [Fact]
    public void Shows_what_the_profile_says()
    {
        var editor = Editor(new ClientProfile
        {
            Name = "one",
            Host = "h",
            Port = "1234",
            Username = "me",
            ClientId = "id",
            AcceptAnyServerCertificate = true,
            TabText = "#111111",
            TabFill = "#222222",
            PageBackground = "#333333",
        }, password: "p", keyPassword: "k");

        Assert.Equal("Client one", editor.Title);
        Assert.False(editor.IsNew);
        Assert.Equal("1234", editor.Port);
        Assert.Equal("me", editor.Username);
        Assert.Equal("p", editor.Password);
        Assert.Equal("k", editor.KeyPassword);
        Assert.True(editor.AcceptAnyServerCertificate);
        Assert.Null(editor.Error);
        Assert.True(editor.SaveCommand.CanExecute(null));
        Assert.False(editor.HasStoreAuthorities);
        Assert.False(editor.HasStoreClients);
        Assert.NotEmpty(editor.Palette);
        Assert.NotNull(editor.PreviewTextBrush);
        Assert.NotNull(editor.PreviewFillBrush);
        Assert.NotNull(editor.PreviewPageBrush);
        Assert.Equal("New client", Editor(isNew: true).Title);
    }

    [Theory]
    [InlineData("Host", "a b", "host name")]
    [InlineData("Port", "0", "port must be")]
    [InlineData("Port", "99999", "port must be")]
    [InlineData("ClientId", "a b", "client ID")]
    [InlineData("TabText", "nope", "colour")]
    [InlineData("TabFill", "nope", "colour")]
    [InlineData("PageBackground", "nope", "colour")]
    public void A_field_that_is_not_good_says_why_and_stops_saving(string property, string value, string expected)
    {
        var editor = Editor();

        typeof(ClientSettingsViewModel).GetProperty(property)!.SetValue(editor, value);

        Assert.Contains(expected, editor.Error);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Names_and_passwords_are_checked_too()
    {
        var editor = Editor(others: ["taken"]);

        editor.Name = "taken";
        Assert.NotNull(editor.Error);
        editor.Name = "free";
        Assert.Null(editor.Error);

        editor.Password = "secret";
        Assert.Equal("A password is only sent with a username", editor.Error);
        editor.Username = "me";
        Assert.Null(editor.Error);
    }

    [Fact]
    public void A_key_needs_its_certificate()
    {
        var editor = Editor();
        editor.ClientKey = "/k.pem";
        Assert.Null(editor.Error);

        editor.UseTls = true;

        Assert.Equal("Choose the client certificate that goes with the private key", editor.Error);
    }

    [Fact]
    public void The_usual_port_follows_tls_unless_another_was_typed()
    {
        var editor = Editor();

        editor.UseTls = true;
        Assert.Equal("8883", editor.Port);
        editor.UseTls = false;
        Assert.Equal("1883", editor.Port);

        editor.Port = "4000";
        editor.UseTls = true;
        Assert.Equal("4000", editor.Port);
        editor.UseTls = false;
        Assert.Equal("4000", editor.Port);
    }

    [Fact]
    public void A_pfx_holds_its_own_key()
    {
        var editor = Editor();
        Assert.True(editor.ShowKeyFile);

        editor.ClientCertificate = "c.pfx";
        editor.ClientKey = "k.pem";

        Assert.False(editor.ShowKeyFile);
        Assert.Equal("", editor.ToProfile().ClientKey);
    }

    [Fact]
    public void The_profile_is_trimmed_and_given_an_id_if_it_has_none()
    {
        var editor = Editor();
        editor.Name = " n ";
        editor.Host = " h ";
        editor.Port = " 1 ";
        editor.Username = " u ";
        editor.ClientId = " ";
        editor.TabFill = "#abcdef";

        var profile = editor.ToProfile();

        Assert.Equal("n", profile.Name);
        Assert.Equal("h", profile.Host);
        Assert.Equal("1", profile.Port);
        Assert.Equal("u", profile.Username);
        Assert.NotEmpty(profile.ClientId);
        Assert.Equal("#ABCDEF", profile.TabFill.ToUpperInvariant());
    }

    [Fact]
    public void Colours_can_be_picked_and_cleared()
    {
        var editor = Editor();

        editor.PickTabTextCommand.Execute("#111111");
        editor.PickTabFillCommand.Execute("#222222");
        editor.PickPageBackgroundCommand.Execute("#333333");

        Assert.Equal("#111111", editor.TabText);
        Assert.Equal("#222222", editor.TabFill);
        Assert.Equal("#333333", editor.PageBackground);

        editor.PickTabTextCommand.Execute("");
        Assert.Equal("", editor.TabText);
    }

    [Theory]
    [InlineData(CertificateFileKind.Authority, "CaCertificate")]
    [InlineData(CertificateFileKind.Client, "ClientCertificate")]
    [InlineData(CertificateFileKind.Key, "ClientKey")]
    public async Task Files_can_be_picked_and_cleared(CertificateFileKind kind, string property)
    {
        var cancel = TestContext.Current.CancellationToken;
        var editor = Editor();
        string Value() => (string)typeof(ClientSettingsViewModel).GetProperty(property)!.GetValue(editor)!;

        await editor.BrowseCommand.ExecuteAsync(kind).WaitAsync(cancel);
        editor.PickFile = _ => Task.FromResult<string?>(null);
        await editor.BrowseCommand.ExecuteAsync(kind).WaitAsync(cancel);
        Assert.Equal("", Value());

        editor.PickFile = k => Task.FromResult<string?>("/picked " + k);
        await editor.BrowseCommand.ExecuteAsync(kind).WaitAsync(cancel);
        Assert.Equal("/picked " + kind, Value());

        editor.ClearFileCommand.Execute(kind);
        Assert.Equal("", Value());
    }

    [Fact]
    public void Clearing_the_client_certificate_takes_its_key_and_password()
    {
        var editor = Editor(keyPassword: "k");
        editor.ClientCertificate = "c.pem";
        editor.ClientKey = "k.pem";

        editor.ClearFileCommand.Execute(CertificateFileKind.Client);

        Assert.Equal("", editor.ClientKey);
        Assert.Equal("", editor.KeyPassword);
    }

    [Fact]
    public void Saving_and_cancelling_close_the_editor()
    {
        var editor = Editor();
        var closed = new List<bool>();
        editor.SaveCommand.Execute(null);
        editor.CancelCommand.Execute(null);
        editor.CloseRequested += closed.Add;

        editor.SaveCommand.Execute(null);
        editor.CancelCommand.Execute(null);

        Assert.Equal([true, false], closed);
    }

    [Fact]
    public void Files_are_read_now_so_a_problem_shows_before_connecting()
    {
        var store = Store();
        var root = Make(store, "root", CertificateKind.RootAuthority);
        var client = Make(store, "client", CertificateKind.Client, root);
        var editor = Editor();

        editor.UseTls = true;
        Assert.Equal("", editor.CertificateInfo);
        Assert.Null(editor.CertificateProblem);

        editor.CaCertificate = root.CertificatePath;
        Assert.Equal("CA: root", editor.CertificateInfo);

        editor.CaCertificate = root.AuthorityPath;
        Assert.StartsWith("CA: ", editor.CertificateInfo);

        var both = _folder.Write("two.pem", File.ReadAllText(root.CertificatePath) + File.ReadAllText(client.CertificatePath));
        editor.CaCertificate = both;
        Assert.Equal("CA: 2 certificates", editor.CertificateInfo);

        editor.ClientCertificate = client.CertificatePath;
        editor.ClientKey = client.KeyPath;
        Assert.Contains("Client certificate: client, valid until ", editor.CertificateInfo);
        Assert.Null(editor.CertificateProblem);

        editor.ClientKey = _folder.At("gone.key");
        Assert.NotNull(editor.CertificateProblem);

        editor.UseTls = false;
        Assert.Null(editor.CertificateProblem);
        Assert.Equal("", editor.CertificateInfo);
    }

    [Fact]
    public void The_certificates_page_offers_its_cas_and_client_certificates()
    {
        var store = Store();
        var root = Make(store, "root", CertificateKind.RootAuthority);
        var client = Make(store, "client", CertificateKind.Client, root);
        var editor = Editor(store: store);
        editor.UseTls = true;

        Assert.True(editor.HasStoreAuthorities);
        Assert.True(editor.HasStoreClients);

        editor.SelectedStoreClient = editor.StoreClients[0];
        Assert.Equal(client.CertificatePath, editor.ClientCertificate);
        Assert.Equal(client.KeyPath, editor.ClientKey);
        Assert.Equal("", editor.KeyPassword);
        Assert.Equal(root.AuthorityPath, editor.CaCertificate);

        editor.SelectedStoreAuthority = editor.StoreAuthorities[0];
        Assert.Equal(root.AuthorityPath, editor.CaCertificate);

        editor.SelectedStoreAuthority = null;
        editor.SelectedStoreClient = null;
        Assert.Equal(client.CertificatePath, editor.ClientCertificate);
    }

    [Fact]
    public void A_file_chosen_another_way_is_not_one_of_the_pages()
    {
        var store = Store();
        var root = Make(store, "root", CertificateKind.RootAuthority);
        var client = Make(store, "client", CertificateKind.Client, root);
        var editor = Editor(store: store);
        editor.SelectedStoreAuthority = editor.StoreAuthorities[0];
        editor.SelectedStoreClient = editor.StoreClients[0];

        editor.CaCertificate = "/elsewhere/ca.pem";
        Assert.Null(editor.SelectedStoreAuthority);
        editor.ClientCertificate = "/elsewhere/c.pem";
        Assert.Null(editor.SelectedStoreClient);

        editor.CaCertificate = root.AuthorityPath;
        Assert.NotNull(editor.SelectedStoreAuthority);
        editor.ClientCertificate = client.CertificatePath;
        Assert.NotNull(editor.SelectedStoreClient);
    }

    [Fact]
    public void Saving_copies_what_was_picked_from_the_page_into_the_clients_own_folder()
    {
        var store = Store();
        var root = Make(store, "root", CertificateKind.RootAuthority);
        var client = Make(store, "client", CertificateKind.Client, root);
        var editor = Editor(store: store);
        editor.UseTls = true;
        editor.SelectedStoreClient = editor.StoreClients[0];
        editor.SelectedStoreAuthority = editor.StoreAuthorities[0];

        editor.SaveCommand.Execute(null);

        Assert.NotEqual(root.AuthorityPath, editor.CaCertificate);
        Assert.NotEqual(client.CertificatePath, editor.ClientCertificate);
        Assert.True(File.Exists(editor.CaCertificate));
        Assert.True(File.Exists(editor.ClientKey));
        var profile = editor.ToProfile();
        Assert.Equal(root.Id, profile.CaCertificateId);
        Assert.Equal(client.Id, profile.ClientCertificateId);

        // Opened again, the copies still count as the certificates they came from.
        var again = Editor(profile, store: store);
        Assert.Equal(root.Id, again.SelectedStoreAuthority!.Id);
        Assert.Equal(client.Id, again.SelectedStoreClient!.Id);
        again.CaCertificate += " ";
        again.ClientCertificate += " ";
        Assert.NotNull(again.SelectedStoreAuthority);
        Assert.NotNull(again.SelectedStoreClient);
    }

    [Fact]
    public void Without_tls_nothing_is_copied_and_no_ids_are_kept()
    {
        var store = Store();
        Make(store, "root", CertificateKind.RootAuthority);
        var editor = Editor(store: store);
        editor.SelectedStoreAuthority = editor.StoreAuthorities[0];
        var path = editor.CaCertificate;

        editor.SaveCommand.Execute(null);

        Assert.Equal(path, editor.CaCertificate);
        Assert.Null(editor.ToProfile().CaCertificateId);
    }

    [Fact]
    public void A_copy_that_fails_keeps_the_editor_open()
    {
        var store = Store();
        Make(store, "root", CertificateKind.RootAuthority);
        var editor = Editor(store: store);
        editor.UseTls = true;
        editor.SelectedStoreAuthority = editor.StoreAuthorities[0];
        Directory.CreateDirectory(_folder.At("store"));
        File.WriteAllText(store.ClientFolder, "a file in the way");
        var closed = false;
        editor.CloseRequested += _ => closed = true;

        editor.SaveCommand.Execute(null);

        Assert.False(closed);
        Assert.NotNull(editor.Error);
    }
}
