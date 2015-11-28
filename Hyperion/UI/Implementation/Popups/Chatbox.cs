﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Popup = Edge.Hyperion.UI.Components.Popup;

namespace Edge.Hyperion.UI.Implementation.Popups {
    public class Chatbox:Popup {
        public Chatbox(Game game, Vector2 location, Int32 width, Int32 height)
            : base(game, location, width, height) {

        }
    }
}
