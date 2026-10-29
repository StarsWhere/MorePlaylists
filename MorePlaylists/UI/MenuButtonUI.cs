using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;
using System;
using Zenject;

namespace MorePlaylists.UI
{
    internal class MenuButtonUI : IInitializable, IDisposable
    {
        private readonly MenuButton menuButton;
        private readonly MenuButtons menuButtons;
        private readonly MainFlowCoordinator mainFlowCoordinator;
        private readonly MorePlaylistsFlowCoordinator morePlaylistsFlowCoordinator;

        public MenuButtonUI(MenuButtons menuButtons, MainFlowCoordinator mainFlowCoordinator, MorePlaylistsFlowCoordinator morePlaylistsFlowCoordinator)
        {
            menuButton = new MenuButton("More Playlists", "Download Playlists", MenuButtonClicked, true);
            this.menuButtons = menuButtons;
            this.mainFlowCoordinator = mainFlowCoordinator;
            this.morePlaylistsFlowCoordinator = morePlaylistsFlowCoordinator;
        }

        public void Initialize()
        {
            menuButtons.RegisterButton(menuButton);
        }

        public void Dispose()
        {
            menuButtons.UnregisterButton(menuButton);
        }

        private void MenuButtonClicked()
        {
            mainFlowCoordinator.PresentFlowCoordinator(morePlaylistsFlowCoordinator);
        }
    }
}
