![The BMS F-16CM-50 converted to Blender with the BMS Suite](https://raw.githubusercontent.com/Danguard63/BMS-suite/main/docs/forum/f16_blender.jpg)

Hi everyone! 👋

To all the theater builders, campaign makers, 3D artists, skinners and to everyone who just loves tinkering with Falcon BMS: I am really happy to share with you something I have been working on for a long time. It's called the **BMS Suite**, and it is a set of editing tools for **Falcon BMS 4.38**, all in one place, **free and open source**.

It started because I wanted to build a new theater and kept jumping between a dozen tools, spreadsheets and hex editors. So I began writing my own helpers… and one helper became seven programs. 😄 Now they all start from one main menu, all open on the theater you are working on, and every program saves what it replaces before touching BMS, so you can always go back.

![BMS Suite main menu](https://raw.githubusercontent.com/Danguard63/BMS-suite/main/docs/guide/shots/01_home.jpg)

### 🧰 What's inside

* **TerrainTool**: builds the New Terrain of a theater from free real-world data (elevation, water, land cover, roads, real airbase positions), ready for Terrain Builder.
* **Objective Studio**: airbases and objectives in 3D. Layouts, pieces, parking, runways aligned to the real heading, bridges; it builds and installs the objects with a backup.
* **Art Editor**: all the UI art of a theater (flags, menu backgrounds, banners, campaign pictures, small maps, patches, fonts, sounds and texts) with a live preview of the screens.
* **Campaign Editor**: campaigns (.cam) and Tactical Engagements (.tac) on the theater map. Units, squadrons, objectives, ownership, priorities, stations and ILS, plus a validator that tells you what BMS will not like.
* **Model Editor**: 3D models in a DirectX view with PBR materials, DOFs, switches, slots (load weapons, put the gear down, turn on the lights), Parent.dat checks, and a browser for the whole theater database.
* **Database Interchange**: copies bases, objective layouts, aircraft, helicopters, ground vehicles, ships, weapons and unit types from one theater to another, with everything they need, renumbered for the destination.

![Objective Studio – airbase in 3D](https://raw.githubusercontent.com/Danguard63/BMS-suite/main/docs/guide/shots/24_os_3d.jpg)

### 🆕 Highlight: export BMS models to Blender

This is the one 3D artists have been asking for since the 3ds Max days. 🎉 The Model Editor now has **Export to Blender…**: pick a model (or a list of model numbers) and you get a ready-to-use **.blend** laid out for the **official BMS Blender plugin**:

* one collection per LOD, already in the plugin's LOD list;
* DOFs, switches and slots as the plugin's objects, with their numbers, limits, multipliers and branches;
* meshes with normals and UVs, PBR materials with albedo, ARMW, normal and emissive textures;
* works with buildings, aircraft, vehicles, ships and weapons, old numbered-texture models included.

Edit it in Blender and export it back with *File → Export → F4-BMS (.bml)*. The picture at the top of this post is the stock F-16CM-50 converted this way and rendered in Blender. 🙂

![Model Editor – DirectX view](https://raw.githubusercontent.com/Danguard63/BMS-suite/main/docs/guide/shots/50_me_3d.jpg)

![Campaign Editor – theater map](https://raw.githubusercontent.com/Danguard63/BMS-suite/main/docs/guide/shots/41_ce_main.jpg)

![Art Editor](https://raw.githubusercontent.com/Danguard63/BMS-suite/main/docs/guide/shots/31_ae_edit.jpg)

### 🛡️ Safety first

The KTO and BMS's shared files are never touched. Every change goes through a plan you can read first, every install makes a backup, and **Restore** puts things back. Still, **please back up your Falcon BMS folder** before installing changes into a theater: better safe than sorry!

### 📥 Download

* **Latest release:** [github.com/Danguard63/BMS-suite/releases/latest](https://github.com/Danguard63/BMS-suite/releases/latest). Download `BMSSuite-x.y.z.zip`, unzip it in a folder you can write to (for example `C:\BMS Suite`) and start `BMSSuite.exe`.
* **Requirements:** Windows 10/11 x64 and the free [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). For the Blender export: Blender 3.6 LTS with the official BMS Blender plugin.
* **User guide (PDF, with screenshots):** inside the zip, or [here](https://github.com/Danguard63/BMS-suite/blob/main/docs/BMS-Suite-User-Guide.pdf).
* **Source code:** [github.com/Danguard63/BMS-suite](https://github.com/Danguard63/BMS-suite), GPL-3.0 license.

### 💬 Feedback is gold

This is an early version, so I am sure you will find things to improve. Please tell me what works, what breaks and what you would love to see next, here in this thread or in the [GitHub issues](https://github.com/Danguard63/BMS-suite/issues). Screenshots and log files are always welcome.

### 🙏 Thanks

A big thank you to **Benchmark Sims** for keeping Falcon alive and for the official Blender plugin, to the author of **Mission Commander**, which inspired the Campaign Editor, and to this community, whose forum posts, guides and add-on theaters taught me most of what I know about BMS.

The BMS Suite is and will stay free. 🙂

Happy building and check six! ✈️
**Danguard63**

*The BMS Suite is a community project and is not affiliated with Benchmark Sims. It contains no BMS files.*
