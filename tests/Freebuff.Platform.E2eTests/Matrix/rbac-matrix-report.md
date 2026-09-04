# RBAC + Module/Package Matrix — Test Oracle Report

_Generated 2026-09-04 00:36:21Z from the live seed + PageRegistry._

**Effective permission formula:** `role grants ∩ company package modules` (SuperAdmin bypasses all checks).

## Coverage
- Total (role × page × action) cells: **924**
- Covered by the effective-permission matrix test: **924**
- Cells with an HTTP endpoint test: **217**
- Uncovered cells: **0** (every (role × page × action) cell is asserted by `RbacMatrixTests.Matrix_EffectivePermissions_Exhaustive`)

## Packages → Modules

| Package | Modules granted |
|---|---|
| Basic | dashboard, fleet |
| Professional | dashboard, fleet, organization |
| Enterprise | dashboard, fleet, organization, platform |

## SuperAdmin  (`admin@freebuff.com`)


| Page | view | create | update | delete | export | import | HTTP endpoint coverage |
|---|---|---|---|---|---|---|---|
| Dashboard (`dashboard`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view |
| Vehicles (`vehicle`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Drivers (`driver`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Geofences (`geofence`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Routes (`route`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Trips (`trip`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Alerts (`alert`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Fuel (`fuel`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Maintenance (`maintenance`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Reports (`report`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Companies (`company`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view |
| Users (`user`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Roles & Permissions (`role`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Localization (`localization`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view |
| Settings (`settings`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Documents (`document`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Subscription (`subscription`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Clients (`client`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Notifications (`notification`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Platform Admin (`platform`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view |
| Packages (`package`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view |
| Modules (`module`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view |

## Company Admin  (`admin@demofleet.com`)

- Company package: **Professional**
- Effective modules: `dashboard, fleet, organization`

| Page | view | create | update | delete | export | import | HTTP endpoint coverage |
|---|---|---|---|---|---|---|---|
| Dashboard (`dashboard`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view |
| Vehicles (`vehicle`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Drivers (`driver`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Geofences (`geofence`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Routes (`route`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Trips (`trip`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Alerts (`alert`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Fuel (`fuel`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Maintenance (`maintenance`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Reports (`report`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Companies (`company`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view |
| Users (`user`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Roles & Permissions (`role`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Localization (`localization`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view |
| Settings (`settings`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Documents (`document`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Subscription (`subscription`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Clients (`client`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Notifications (`notification`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Platform Admin (`platform`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Packages (`package`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Modules (`module`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |

## Fleet Manager  (`e2e.fleetmanager@demo.test`)

- Company package: **Professional**
- Effective modules: `dashboard, fleet, organization`

| Page | view | create | update | delete | export | import | HTTP endpoint coverage |
|---|---|---|---|---|---|---|---|
| Dashboard (`dashboard`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Vehicles (`vehicle`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Drivers (`driver`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Geofences (`geofence`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Routes (`route`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Trips (`trip`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Alerts (`alert`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Fuel (`fuel`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Maintenance (`maintenance`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Reports (`report`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Companies (`company`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Users (`user`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Roles & Permissions (`role`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Localization (`localization`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Settings (`settings`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Documents (`document`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Subscription (`subscription`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Clients (`client`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Notifications (`notification`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Platform Admin (`platform`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Packages (`package`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Modules (`module`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |

## Read Only  (`e2e.readonly@demo.test`)

- Company package: **Professional**
- Effective modules: `dashboard, fleet, organization`

| Page | view | create | update | delete | export | import | HTTP endpoint coverage |
|---|---|---|---|---|---|---|---|
| Dashboard (`dashboard`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Vehicles (`vehicle`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Drivers (`driver`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Geofences (`geofence`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Routes (`route`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Trips (`trip`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Alerts (`alert`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Fuel (`fuel`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Maintenance (`maintenance`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Reports (`report`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Companies (`company`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Users (`user`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Roles & Permissions (`role`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Localization (`localization`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Settings (`settings`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Documents (`document`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Subscription (`subscription`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Clients (`client`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Notifications (`notification`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Platform Admin (`platform`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Packages (`package`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Modules (`module`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |

## Ops Manager  (`e2e.ops@demo.test`)

- Company package: **Professional**
- Effective modules: `dashboard, fleet, organization`

| Page | view | create | update | delete | export | import | HTTP endpoint coverage |
|---|---|---|---|---|---|---|---|
| Dashboard (`dashboard`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Vehicles (`vehicle`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Drivers (`driver`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Geofences (`geofence`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Routes (`route`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Trips (`trip`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Alerts (`alert`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Fuel (`fuel`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Maintenance (`maintenance`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Reports (`report`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Companies (`company`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Users (`user`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Roles & Permissions (`role`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Localization (`localization`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Settings (`settings`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Documents (`document`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Subscription (`subscription`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Clients (`client`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Notifications (`notification`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Platform Admin (`platform`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Packages (`package`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Modules (`module`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |

## Basic Admin  (`e2e.admin@basic.test`)

- Company package: **Basic**
- Effective modules: `dashboard, fleet`

| Page | view | create | update | delete | export | import | HTTP endpoint coverage |
|---|---|---|---|---|---|---|---|
| Dashboard (`dashboard`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view |
| Vehicles (`vehicle`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Drivers (`driver`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Geofences (`geofence`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Routes (`route`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | view, create, update, delete |
| Trips (`trip`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Alerts (`alert`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Fuel (`fuel`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Maintenance (`maintenance`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Reports (`report`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Companies (`company`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Users (`user`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Roles & Permissions (`role`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Localization (`localization`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Settings (`settings`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Documents (`document`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Subscription (`subscription`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Clients (`client`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Notifications (`notification`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Platform Admin (`platform`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Packages (`package`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Modules (`module`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |

## Basic Viewer  (`e2e.viewer@basic.test`)

- Company package: **Basic**
- Effective modules: `dashboard, fleet`

| Page | view | create | update | delete | export | import | HTTP endpoint coverage |
|---|---|---|---|---|---|---|---|
| Dashboard (`dashboard`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Vehicles (`vehicle`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Drivers (`driver`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Geofences (`geofence`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Routes (`route`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Trips (`trip`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Alerts (`alert`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Fuel (`fuel`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Maintenance (`maintenance`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Reports (`report`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Companies (`company`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Users (`user`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Roles & Permissions (`role`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view, create, update, delete |
| Localization (`localization`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Settings (`settings`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Documents (`document`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Subscription (`subscription`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Clients (`client`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Notifications (`notification`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |
| Platform Admin (`platform`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Packages (`package`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |
| Modules (`module`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | view |

## Notes
- `export` / `import` have **no dedicated HTTP endpoints** in the current API — they are gated at the permission-calculation and selector layers (see `RbacEdgeCaseTests.Edge_ExportImport_GatedLikeOtherActions`).
- Planned pages (`trip`, `alert`, `fuel`, `maintenance`, `report`, `client`, `notification`) grant nothing to tenants at any layer.
- `/tenant/drivers` and `/tenant/clients` are dropdown helpers open to any authenticated user (tenant-scoped by design), not the Drivers/Clients page surface.
