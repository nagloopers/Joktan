using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Certificates.ViewModels;
using Xunit;

namespace Joktan.Certificates.Tests.ViewModels;

public class CertificatesViewModelTests : CertificateTestBase
{
    private CertificatesViewModel Page(Func<StoredCertificate, List<string>>? usedBy = null) => new(Store, usedBy ?? (_ => []));

    private static CertificateNodeViewModel Node(CertificatesViewModel vm, string name) =>
        vm.Roots.SelectMany(r => r.SelfAndDescendants()).First(n => n.Name == name);

    // As if the CA that signed it had been taken out of the list by hand.
    private StoredCertificate Orphan(StoredCertificate entry)
    {
        File.WriteAllText(Store.IndexPath, File.ReadAllText(Store.IndexPath)
            .Replace($"\"IssuerId\": \"{entry.IssuerId}\"", "\"IssuerId\": \"gone\""));
        return Reload(entry.Id);
    }

    [Fact]
    public void Starts_empty_with_nothing_selected()
    {
        var vm = Page();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasSelection);
        Assert.Equal("", vm.DeleteText);
        Assert.Equal(Store.IndexPath, vm.IndexPath);
    }

    [Fact]
    public void Shows_certificates_below_the_ca_that_signed_them()
    {
        var root = Root();
        Server(root);
        var vm = Page(e => e.Name == "server" ? ["a client"] : []);

        var top = Assert.Single(vm.Roots);
        Assert.Equal("root", top.Name);
        Assert.Equal("server", top.Children.Single().Name);
        Assert.Equal("a client", top.Children.Single().UsedBy);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void A_certificate_whose_ca_has_gone_is_shown_at_the_top()
    {
        var entry = Server(Root());
        Orphan(entry);

        var vm = Page();

        Assert.Contains(vm.Roots, n => n.Name == "server");
    }

    [Fact]
    public void Refreshing_keeps_the_selection_and_what_was_collapsed()
    {
        var root = Root();
        Server(root);
        var vm = Page();
        vm.Roots[0].IsExpanded = false;
        vm.Selected = Node(vm, "server");

        vm.Refresh();

        Assert.False(vm.Roots[0].IsExpanded);
        Assert.Equal("server", vm.Selected!.Name);
    }

    [Fact]
    public void Selecting_fills_the_rename_box_and_ends_a_delete()
    {
        Root();
        var vm = Page();
        vm.IsConfirmingDelete = true;
        Assert.False(vm.DeleteCommand.CanExecute(null));

        vm.Selected = vm.Roots[0];

        Assert.Equal("root", vm.RenameText);
        Assert.False(vm.IsConfirmingDelete);
        Assert.True(vm.HasSelection);
        Assert.True(vm.DeleteCommand.CanExecute(null));

        vm.Selected = null;
        Assert.Equal("", vm.RenameText);
    }

    [Fact]
    public void Deleting_says_what_goes_and_asks_first()
    {
        var root = Root();
        Server(root);
        var vm = Page();

        vm.Selected = Node(vm, "server");
        Assert.Equal("Delete server, with their private keys? This cannot be undone.", vm.DeleteText);

        vm.Selected = Node(vm, "root");
        Assert.Contains("and the 1 certificate it signed", vm.DeleteText);

        Client(root);
        vm.Refresh();
        Assert.Contains("and the 2 certificates below it", vm.DeleteText);

        vm.DeleteCommand.Execute(null);
        Assert.True(vm.IsConfirmingDelete);
        vm.CancelDeleteCommand.Execute(null);
        Assert.False(vm.IsConfirmingDelete);
    }

    [Fact]
    public void Deleting_removes_the_certificate_and_what_it_signed()
    {
        var root = Root();
        Server(root);
        Client(root);
        var vm = Page();

        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal(3, Store.Entries.Count);

        vm.Selected = Node(vm, "server");
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal("Deleted server", vm.Message);
        Assert.Null(vm.Selected);

        vm.Selected = Node(vm, "root");
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal("Deleted root and 1 more", vm.Message);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public void Renaming_keeps_a_good_name_and_reports_a_bad_one()
    {
        Root();
        Root("other");
        var vm = Page();

        vm.RenameCommand.Execute(null);

        vm.Selected = Node(vm, "root");
        vm.RenameCommand.Execute(null);
        Assert.Equal("root", vm.Selected!.Name);

        vm.RenameText = "other";
        vm.RenameCommand.Execute(null);
        Assert.NotNull(vm.Error);

        vm.RenameText = "renamed";
        vm.RenameCommand.Execute(null);
        Assert.Null(vm.Error);
        Assert.Equal("renamed", vm.Selected!.Name);
    }

    [Fact]
    public async Task A_new_ca_is_made_through_the_editor_and_selected()
    {
        var vm = Page();
        var cancel = TestContext.Current.CancellationToken;
        await vm.NewAuthorityCommand.ExecuteAsync(null).WaitAsync(cancel);
        Assert.True(vm.IsEmpty);

        vm.EditNewCertificate = _ => Task.FromResult(false);
        await vm.NewAuthorityCommand.ExecuteAsync(null).WaitAsync(cancel);
        Assert.True(vm.IsEmpty);

        vm.Error = "old";
        vm.EditNewCertificate = editor =>
        {
            editor.Name = "made";
            editor.SaveCommand.Execute(null);
            return Task.FromResult(true);
        };
        await vm.NewAuthorityCommand.ExecuteAsync(null).WaitAsync(cancel);

        Assert.Equal("made", vm.Selected!.Name);
        Assert.Equal("Made the root CA made", vm.Message);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Certificates_are_issued_only_by_a_ca_that_can_and_are_shown_opened_up()
    {
        var cancel = TestContext.Current.CancellationToken;
        Root();
        var vm = Page();
        Assert.False(vm.IssueCommand.CanExecute(null));

        vm.Selected = Node(vm, "root");
        Assert.True(vm.IssueCommand.CanExecute(null));
        await vm.IssueCommand.ExecuteAsync(CertificateKind.Client).WaitAsync(cancel);

        CertificateKind? asked = null;
        vm.EditNewCertificate = editor =>
        {
            asked = editor.SelectedKind.Value;
            editor.Name = "issued";
            editor.SaveCommand.Execute(null);
            return Task.FromResult(true);
        };
        vm.Selected!.IsExpanded = false;
        await vm.IssueCommand.ExecuteAsync(CertificateKind.Client).WaitAsync(cancel);

        Assert.Equal(CertificateKind.Client, asked);
        Assert.Equal("issued", vm.Selected!.Name);
        Assert.Equal("Made issued, signed by root", vm.Message);
        Assert.True(Node(vm, "root").IsExpanded);
        Assert.False(vm.IssueCommand.CanExecute(null));

        vm.Selected = null;
        await vm.IssueCommand.ExecuteAsync(CertificateKind.Client).WaitAsync(cancel);
    }

    [Fact]
    public async Task A_ca_that_has_expired_says_why_it_cannot_sign()
    {
        var now = DateTimeOffset.Now;
        WithDates(Root(), now.AddDays(-10), now.AddDays(-1));
        var vm = Page();
        vm.Selected = vm.Roots[0];
        vm.EditNewCertificate = _ => throw new InvalidOperationException("It should not get this far.");

        await vm.IssueCommand.ExecuteAsync(null).WaitAsync(TestContext.Current.CancellationToken);

        Assert.Contains("expired on", vm.Error);
    }

    [Fact]
    public async Task Exporting_says_what_was_written_where()
    {
        var cancel = TestContext.Current.CancellationToken;
        Root();
        var vm = Page();
        Assert.False(vm.ExportCommand.CanExecute(null));
        vm.Selected = vm.Roots[0];
        Assert.True(vm.ExportCommand.CanExecute(null));
        await vm.ExportCommand.ExecuteAsync(null).WaitAsync(cancel);

        vm.EditExport = _ => Task.FromResult(false);
        await vm.ExportCommand.ExecuteAsync(null).WaitAsync(cancel);
        Assert.Null(vm.Message);

        var target = Folder.At("out");
        Directory.CreateDirectory(target);
        vm.Error = "old";
        vm.EditExport = editor =>
        {
            editor.Folder = target;
            editor.ExportCommand.Execute(null);
            return Task.FromResult(true);
        };
        await vm.ExportCommand.ExecuteAsync(null).WaitAsync(cancel);

        Assert.Contains(target, vm.Message);
        Assert.Null(vm.Error);

        vm.Selected = null;
        await vm.ExportCommand.ExecuteAsync(null).WaitAsync(cancel);
    }

    [Fact]
    public void A_client_is_offered_the_usable_cas_and_client_certificates()
    {
        var root = Root();
        Client(root);
        Server(root);
        Client(root, "second");
        var bad = Root("bad");
        File.Delete(bad.KeyPath);
        Reload(root.Id);
        var (authorities, clients) = CertificatesViewModel.ChoicesFor(Store);

        Assert.Equal(["root"], authorities.Select(a => a.Name));
        Assert.Equal(["client", "second"], clients.Select(c => c.Name));
        var client = clients[0];
        Assert.Equal("root", client.IssuerName);
        Assert.Equal(root.AuthorityPath, client.AuthorityPath);
        Assert.Equal("client (signed by root)", client.ToString());
        Assert.Equal("root", authorities[0].ToString());
    }

    [Fact]
    public void A_client_certificate_whose_ca_has_gone_offers_its_own_ca_file()
    {
        var entry = Client(Root());
        entry = Orphan(entry);

        var clients = CertificatesViewModel.ChoicesFor(Store).Clients;

        var choice = Assert.Single(clients);
        Assert.Null(choice.IssuerName);
        Assert.Equal(entry.AuthorityPath, choice.AuthorityPath);
    }
}
