// Shared HR namespaces, available to every file compiled with this package.
// Hosts compile this file into their own assembly. Program.cs sits outside the HrCore
// namespace, so this using has to stay even though every file in this folder is already inside it.
#pragma warning disable IDE0005
global using HrCore;
#pragma warning restore IDE0005
global using HrCore.Data;
global using HrCore.Domain;
global using HrCore.Infrastructure;
