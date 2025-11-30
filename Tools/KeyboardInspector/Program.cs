using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.UI;

Console.WriteLine("Initializing Main...");
Main main = new();
Console.WriteLine(Main.instance is null ? "Main instance missing" : "Main instance present");
