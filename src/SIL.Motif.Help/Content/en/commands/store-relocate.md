# Point a copied store at its new project path

`store relocate` points a project's Motif store at the project's new path. Use it after a project and its `.motif.db` file were copied or moved together, when other commands refuse the store as belonging to a different project.

```powershell
motif store relocate --project "D:\Projects\Koro\Koro.fwdata" --confirm "Point this Motif store at this project's new path?"
```

Pass the project at its new path. The store is the `.motif.db` file beside it. Without the confirmation phrase the command changes nothing and shows the phrase to pass.

The command relocates a store only to the same project. It compares the new file's FieldWorks language project GUID with the GUID in the store's Baselines or, when the store holds none, in the project file at the old path if that still exists. Different GUIDs refuse with `store.other-project` and leave the store untouched. When neither exists to compare, the project name the store is bound to decides, and the result says so (`identityEvidence` is `name`, not `project-guid`).

It refuses while another program has the project open, and while any Motif process has the store open. Close FieldWorks and Motif first.

The change and a record of it commit together: the store keeps the old path, the new path and the time in its `StoreRelocations` history. The result reports both paths, the time, and `changed`, which is `false` when the store already pointed at the new path. The project and the original copy's store are not touched.
