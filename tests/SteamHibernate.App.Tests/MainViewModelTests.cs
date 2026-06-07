using SteamHibernate.App.ViewModels;
using SteamHibernate.Core.Steam;
using Xunit;

public class MainViewModelTests
{
    [Fact]
    public void Rows_reflect_installed_and_archived_state()
    {
        var lib = new SteamLibrary("/root");
        var installed = new List<InstalledGame>
        {
            new("70", "Half-Life", "/root/steamapps/common/Half-Life", 5, null, lib),
        };
        var archivedIds = new HashSet<string> { "999" };

        var vm = new MainViewModel();
        vm.LoadRows(installed, archivedIds,
            archivedNames: new Dictionary<string,string> { ["999"] = "Old Game" });

        Assert.Contains(vm.Games, r => r.AppId == "70" && r.Status == "Installed");
        Assert.Contains(vm.Games, r => r.AppId == "999" && r.Status == "Archived");
    }
}
