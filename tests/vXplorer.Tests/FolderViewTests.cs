using vXplorer.Core;
using Xunit;

namespace vXplorer.Tests
{
    public class FolderViewTests
    {
        static AppSettings With(params (string path, string view, bool sub)[] views)
        {
            var s = new AppSettings();
            foreach (var v in views) s.FolderViews[AppSettings.FolderViewKey(v.path)] = new FolderView { View = v.view, Subfolders = v.sub };
            return s;
        }

        [Fact]
        public void A_folder_s_own_view_comes_first()
        {
            var s = With((@"C:\Users\v", "Large", true), (@"C:\Users\v\Pictures", "Details", false));
            Assert.Equal("Details", s.FolderViewFor(@"C:\Users\v\Pictures").View);
            Assert.Equal("Large", s.FolderViewFor(@"C:\USERS\V\").View);
        }

        [Fact]
        public void Sub_folders_take_the_nearest_rule_above()
        {
            var s = With((@"C:\Users\v", "Large", true), (@"C:\Users\v\Pictures", "ExtraLarge", true));
            Assert.Equal("Large", s.FolderViewFor(@"C:\Users\v\Documents\Work").View);
            Assert.Equal("ExtraLarge", s.FolderViewFor(@"C:\Users\v\Pictures\2024\Trip").View);
        }

        [Fact]
        public void A_view_for_one_folder_only_is_not_passed_down()
        {
            var s = With((@"C:\Users\v", "Large", true), (@"C:\Users\v\Pictures", "Details", false));
            Assert.Equal("Large", s.FolderViewFor(@"C:\Users\v\Pictures\2024").View);   // Pictures' own is for Pictures only
            Assert.Null(With((@"D:\Media", "Large", false)).FolderViewFor(@"D:\Media\Films"));
        }

        [Fact]
        public void Drive_roots_and_shares_work()
        {
            Assert.Equal("Large", With((@"D:\", "Large", true)).FolderViewFor(@"D:\Media\Films").View);
            Assert.Equal("Large", With((@"\\nas\share", "Large", true)).FolderViewFor(@"\\nas\share\a\b").View);
            Assert.Null(new AppSettings().FolderViewFor(@"C:\Windows"));
        }
    }
}
