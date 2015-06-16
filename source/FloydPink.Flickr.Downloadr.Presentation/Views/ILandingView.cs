﻿namespace FloydPink.Flickr.Downloadr.Presentation.Views {
    using System.Collections.Generic;
    using Model;
    using Model.Enums;

    public interface ILandingView : IBaseView {
        User User { get; set; }
        Preferences Preferences { get; set; }
        string Page { get; set; }
        string Pages { get; set; }
        string PerPage { get; set; }
        string Total { get; set; }
        IEnumerable<Photoset> Albums { get; set; }
        void UpdateProgress(string percentDone, string operationText, bool cancellable);
    }
}
