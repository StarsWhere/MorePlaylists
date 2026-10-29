using MorePlaylists.Sources;
using MorePlaylists.UI;
using SiraUtil.Web;
using System.Collections.Generic;
using UnityEngine;

namespace MorePlaylists.AccSaber
{
    internal class AccSaber : BasicSource<AccSaberEntry>
    {
        private Sprite? logo;
        protected override string Website => "https://api.accsaber.com/v1/";
        protected override string Endpoint => "playlists/overall";
        protected override IEnumerable<string> Endpoints => new[]
        {
            Website + "playlists/overall",
            Website + "playlists/true_acc",
            Website + "playlists/standard_acc",
            Website + "playlists/tech_acc"
        };
        public override Sprite Logo
        {
            get
            {
                if (logo == null)
                {
                    logo = PlaylistMods.Common.SpriteLoader.FromResource(typeof(AccSaber).Assembly, "MorePlaylists.Images.AccSaber.png");
                }
                return logo;
            }
        }

        public AccSaber(IHttpService siraHttpService, BasicListViewController listViewController, BasicDetailViewController detailViewController) : base(siraHttpService, listViewController, detailViewController)
        {
        }
    }
}
