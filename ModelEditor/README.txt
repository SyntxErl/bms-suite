BMS Model Editor
================

3D models of Falcon BMS 4.38 (Data\Terrdata\objects\Models\<number>): a separate program, independent of the
Object Editor and of the Campaign Editor (their 3D view and texture windows stay where they are).

  List (left)          aircraft, helicopters, ground vehicles, ships and weapons from the BMS database
                       (Falcon4_CT / VCD / WCD), or every model folder by number. Search by name or model number.
  3D view              LOD and livery, DOF sliders with their names (gear, flaps, canopy, ...), switches
                       on / off, weapons mounted on the slots, slot markers, front / side / top views,
                       Screenshot... (PNG at twice the window size, default folder Screenshots).
  Parent.dat           text editor with "Check data" (LOD files, Dofs / Switches numbers, slots, liveries,
                       textures, radius) and "Suggest values" (Dofs, Switches and radius computed from the model).
                       Save writes into BMS: BMS must be closed, the old file is copied to Backup first.
  Info                 who uses the model (CT entries), files, every LOD (triangles, DOFs, switches, slots),
                       materials and textures.
  Open folder...       opens a model folder outside BMS, for example a Blender export (Parent.dat, Model_N.bml,
                       Materials.mtl, textures): 3D view, DOFs, switches, slots, Check data, Info. You can also drag
                       the folder onto the window. Nothing is copied into BMS; shared textures (e.g. "1909/PilotUS_ALB.dds")
                       and numbered textures are looked for in BMS too. Save in the Parent.dat tab writes in that folder.
  Export to Blender... converts the model shown, or several model numbers (1052, 917, 1700-1705), to a Blender .blend
                       for the official BMS Blender plugin (bms-blender-plugin): one collection per LOD (in the plugin's
                       LOD list), DOFs / switches / slots as the plugin's objects with their numbers and values, meshes
                       with normals and UVs, materials with their textures (PNG, in nodes the plugin's exporter reads).
                       Materials.mtl and Parent.dat are copied next to the .blend. Needs Blender 3.6 LTS with the plugin
                       installed; Blender runs in the background. Switch branches that start off are hidden (Alt+H in
                       Blender shows them). A switch inside a DOF is written as the same DOF inside the switch (BMS draws
                       it the same way; the plugin's exporter needs it so). Nothing in BMS is changed.
  Check all models...  checks every model folder and lists the problems; double-click opens the model;
                       Export... writes a tab separated text file.
  Backups...           every file changed by the Model Editor, with Restore.

Everything is read only except "Save" in the Parent.dat tab. Before changing files inside BMS make a backup of
your BMS Data folder.

Data\DOF.xml and Data\switch.xml (names of DOFs and switches) come from the official BMS Blender exporter
(bms-blender-plugin, Copyright (c) 2023 Benchmark Sims, MIT license: Data\LICENSE-bms-blender-plugin.txt).
How DOFs, switches and slots work in a BML file was also taken from that exporter.

Build:  build.bat   (needs the .NET 8 SDK; the shared library is ..\TerrainTool\src\TerrainTool.Core)
Start:  run.bat     or  run.bat --model 1502   or  run.bat --data "D:\Falcon BMS 4.38\Data" --model 724
