# SAM Simulation Engine Roadmap

**Status:** Draft  
**Last reviewed:** 2026-07-01  
**Repository:** `SAM-BIM/SAM_OpenStudio`  
**Target branch:** `sow/2026-Q3`  
**Audience:** SAM contributors, computational engineering team, MEP simulation developers  
**Scope:** Strategic roadmap for extending SAM beyond the current TAS EDSL simulation route while keeping TAS as the production-grade engine.

---

## 1. Executive Summary

SAM currently benefits from a strong integration with **TAS EDSL**, mainly because TAS is fast, mature, practical to automate from C#, and already aligned with established project workflows.

The recommendation is **not** to replace TAS. Instead, SAM should move toward a **multi-engine simulation architecture**:

```text
SAM AnalyticalModel
        |
        v
SAM Simulation Abstraction
        |
        v
 +-------------+----------------+---------------------+
 | SAM_Tas     | SAM_OpenStudio | Future: Modelica/FMI |
 | production  | open validation| controls / MPC       |
 +-------------+----------------+---------------------+
```

The strategic goal is to make SAM **engine-independent**.

TAS should remain the main production route. OpenStudio and EnergyPlus should become the open, research-aligned, AI-friendly, validation-capable route. Modelica/FMI-based workflows should be monitored and prepared for longer-term advanced HVAC, controls, model predictive control, and digital twin use cases.

---

## 2. Strategic Position

### 2.1 Keep TAS as the production engine

TAS should remain the main production simulation backend for SAM where the priority is:

- speed;
- reliability;
- established project workflows;
- UK compliance familiarity;
- current SAM integration;
- direct API access from C#;
- practical model exchange with existing engineering processes.

### 2.2 Add OpenStudio/EnergyPlus as a second simulation backend

OpenStudio and EnergyPlus should be introduced as a parallel route where the priority is:

- open-source transparency;
- academic and research alignment;
- repeatable validation;
- AI-assisted model generation and QA;
- public benchmark comparisons;
- easier collaboration with external research partners;
- support for OpenStudio Measures and parametric workflows.

OpenStudio is especially relevant for this repository because the OpenStudio SDK exposes model workflows through C#, while EnergyPlus provides the underlying whole-building simulation engine.

### 2.3 Prepare for Modelica/FMI later

Modelica/FMI should not be the first implementation priority, but SAM should be designed so that this route remains possible later.

This matters for:

- detailed HVAC system dynamics;
- control strategy testing;
- model predictive control;
- digital twins;
- co-simulation;
- advanced plant and control logic;
- integration with BOPTEST-style benchmarking.

---

## 3. Recommended Architecture

SAM should define a neutral simulation contract that sits above individual engines.

```text
SAM AnalyticalModel
        |
        v
SAM Simulation Intent
        |
        v
Engine Adapter
        |
        v
Simulation Engine
        |
        v
SAM Results
```

### 3.1 Core simulation contract

The engine-neutral contract should describe the engineering intent before it is translated into engine-specific objects.

Minimum contract:

- analytical model;
- location and weather;
- spaces and zones;
- geometry;
- constructions;
- apertures;
- shading;
- schedules;
- internal gains;
- infiltration;
- ventilation;
- HVAC intent;
- simulation settings;
- output requests;
- warnings and errors;
- result schema.

### 3.2 Adapter responsibilities

Each engine adapter should be responsible for:

- translating SAM objects into engine-specific inputs;
- recording assumptions made during translation;
- exposing unsupported features clearly;
- running or preparing simulations;
- collecting results;
- mapping results back into SAM result objects;
- producing QA/QC messages.

### 3.3 Proposed adapters

```text
SAM_Tas
    Current production adapter.

SAM_OpenStudio
    Higher-level OpenStudio SDK route using model objects, measures, standards workflows,
    and EnergyPlus translation.

SAM_EnergyPlus
    Lower-level direct EnergyPlus route using IDF or epJSON where direct control is better.

SAM_FMI / SAM_Modelica
    Future route for controls, dynamic systems, co-simulation, and model predictive control.
```

---

## 4. Roadmap Summary

| Phase | Theme | Main outcome |
|---|---|---|
| Phase 1 | Minimal OpenStudio/EnergyPlus proof of concept | Generate a valid OpenStudio/EnergyPlus model from SAM for simple zone loads — **done (MVP, PR #7)** |
| Phase 2 | OpenStudio adapter structure | Establish reusable C# adapter classes and model translation rules — **done (analytical completeness programme, see `openstudio-analytical-completeness-status.md`: full non-HVAC `AnalyticalModel` translation with enforced coverage, results mapping and cancellable execution)** |
| Phase 3 | Validation harness | Compare SAM_Tas and SAM_OpenStudio/EnergyPlus results consistently |
| Phase 4 | AI-assisted QA and model intelligence | Use AI to inspect, explain, repair, and document simulation models safely |
| Phase 5 | Advanced HVAC, controls, Modelica/FMI | Prepare SAM for dynamic controls, co-simulation, MPC, and digital twin workflows |

---

## 5. Phase 1 - SAM_OpenStudio / EnergyPlus Proof of Concept

### Goal

Create a minimal working route from SAM AnalyticalModel to an OpenStudio/EnergyPlus simulation.

### Scope

Start deliberately small. Avoid detailed HVAC initially.

Include:

- one or more spaces/zones;
- basic geometry;
- opaque constructions;
- windows and doors;
- weather file reference;
- simple internal gains;
- simple schedules;
- infiltration or outdoor air assumptions;
- ideal loads air system;
- annual heating and cooling load outputs.

### Deliverables

- `SAM AnalyticalModel -> OpenStudio Model` prototype.
- EnergyPlus simulation run from generated OpenStudio model.
- Basic result extraction back to SAM objects.
- Clear assumptions report.
- Minimal validation example.

### Acceptance criteria

- A simple SAM test model can generate a valid OpenStudio model.
- The generated model can be translated and simulated by EnergyPlus.
- Annual heating and cooling loads can be read back into SAM.
- Translation assumptions are visible to the user or developer.

### Risks

- Geometry translation mismatches.
- Difference between TAS and EnergyPlus thermal assumptions.
- Oversimplified HVAC leading to misleading comparison.
- Weather and design-day handling differences.

---

## 6. Phase 2 - Adapter Structure and OpenStudio SDK Integration

### Goal

Move from proof of concept to a maintainable adapter structure.

### Scope

Define a clear C# architecture for OpenStudio model generation.

Suggested namespaces/classes:

```text
SAM.OpenStudio
SAM.OpenStudio.Convert
SAM.OpenStudio.Create
SAM.OpenStudio.Query
SAM.OpenStudio.Run
SAM.OpenStudio.Results
SAM.OpenStudio.Validation
```

Possible high-level classes:

```text
OpenStudioSimulationSettings
OpenStudioTranslationOptions
OpenStudioTranslationReport
OpenStudioUnsupportedFeature
OpenStudioResult
OpenStudioResultReader
OpenStudioSimulationRunner
```

### Work packages

#### 6.1 Translation options

Define options for:

- simulation period;
- timestep;
- design-day handling;
- weather file path;
- ideal loads vs detailed HVAC;
- solar distribution;
- surface matching tolerance;
- output variables;
- result frequency.

#### 6.2 Translation report

Every export should produce a report containing:

- created OpenStudio objects;
- skipped SAM objects;
- approximated assumptions;
- unsupported features;
- warnings;
- fatal errors.

#### 6.3 Model generation conventions

Define stable naming conventions for generated objects:

```text
SAM Space        -> OpenStudio Space
SAM Zone         -> OpenStudio ThermalZone
SAM Panel        -> OpenStudio Surface
SAM Aperture     -> OpenStudio SubSurface
SAM Material     -> OpenStudio Material
SAM Construction -> OpenStudio Construction
```

#### 6.4 Result schema

Define a SAM result schema that is independent of OpenStudio/EnergyPlus output file formats.

Minimum outputs:

- annual heating load;
- annual cooling load;
- peak heating load;
- peak cooling load;
- zone air temperature;
- zone operative temperature, if available;
- unmet hours;
- solar gains;
- ventilation/infiltration load contribution, where available.

### Deliverables

- Reusable adapter classes.
- Translation report object.
- Result object model.
- Unit tests for translation of geometry, constructions, schedules, and loads.
- Example model committed to the repository.

### Acceptance criteria

- The adapter can translate repeated test cases without manual intervention.
- Unsupported features are reported rather than silently ignored.
- Generated names are stable between runs.
- Results can be compared across simulation engines through a common SAM schema.

---

## 7. Phase 3 - Validation Harness

### Goal

Create a repeatable comparison framework between SAM_Tas and SAM_OpenStudio/EnergyPlus.

### Scope

The validation harness should compare selected models using controlled assumptions.

Initial comparison set:

```text
Case 01 - Single box, no windows
Case 02 - Single box with glazing
Case 03 - Multi-zone model
Case 04 - Shading-sensitive model
Case 05 - Lightweight vs heavyweight construction
Case 06 - High infiltration case
Case 07 - Internal gains and schedules
Case 08 - Weather sensitivity case
```

### Comparison metrics

- annual heating load;
- annual cooling load;
- peak heating load;
- peak cooling load;
- hourly zone temperature;
- unmet hours;
- solar gains;
- ventilation loads;
- simulation runtime;
- warning/error count.

### Deliverables

- Validation test models.
- Automated comparison runner.
- Tolerance definition by metric.
- Result dashboard or Markdown report.
- Regression baseline.

### Acceptance criteria

- Validation can be run by developers without manual setup beyond documented dependencies.
- Differences between engines are classified as expected, suspicious, or failing.
- Each comparison includes an explanation of known modelling differences.

---

## 8. Phase 4 - AI-Assisted QA and Model Intelligence

### Goal

Use AI to make simulation workflows more transparent, faster to debug, and easier to validate, while keeping deterministic SAM validation as the source of truth.

AI should assist the engineer. It should not become the authority that decides whether a model is correct.

Recommended control pattern:

```text
AI proposes -> SAM validates -> Engine runs -> Tests compare -> Engineer approves
```

### 8.1 Why this phase matters

OpenStudio and EnergyPlus are well suited to AI-assisted workflows because their inputs and outputs are highly structured and text/schema based. This makes it realistic to ask AI to inspect generated models, read warnings, propose corrections, generate reports, and explain differences between simulation engines.

For SAM, this phase could become a major differentiator: engineers would not only run simulations, they would receive an explainable QA report describing the model, assumptions, risks, warnings, and likely reasons for unexpected results.

### 8.2 Core AI use cases

#### 8.2.1 Model QA summary

Generate a human-readable summary of the model before simulation:

- number of spaces and zones;
- floor area;
- exposed facade area;
- glazing ratio;
- construction assumptions;
- internal gains;
- schedules;
- ventilation and infiltration assumptions;
- HVAC simplifications;
- simulation period;
- missing or defaulted inputs.

Expected output:

```text
This model contains 24 SAM spaces mapped to 18 OpenStudio thermal zones.
12 apertures have no explicit frame construction and will use the default glazing assumption.
5 internal partitions were treated as adiabatic because no adjacent zone was resolved.
```

#### 8.2.2 Translation QA

AI can review the `OpenStudioTranslationReport` and produce a clearer engineering explanation.

Examples:

- explain unsupported features;
- highlight approximations;
- group repeated warnings;
- separate fatal issues from acceptable assumptions;
- suggest what the engineer should check before trusting results.

This should sit on top of deterministic validation rules, not replace them.

#### 8.2.3 EnergyPlus warning and error interpretation

EnergyPlus often produces detailed warnings that are useful but hard to interpret quickly.

AI can help by:

- grouping repeated warnings;
- identifying likely root causes;
- identifying whether warnings are geometry, material, weather, sizing, HVAC, or output related;
- suggesting likely SAM-side fixes;
- linking warnings back to SAM object names where possible.

Example categories:

```text
Geometry issue
Construction/material issue
Schedule issue
Weather/design-day issue
HVAC sizing issue
Output request issue
Simulation convergence issue
```

#### 8.2.4 Result explanation

AI can compare results and explain them in engineering language:

- why one zone has high peak cooling;
- why heating demand increased after a construction change;
- why TAS and EnergyPlus results diverge;
- whether a result looks plausible compared with model inputs;
- which assumptions are most likely driving the result.

#### 8.2.5 Automatic assumptions report

Generate a report suitable for project records:

- model source;
- simulation engine;
- assumptions made during translation;
- limitations;
- outputs requested;
- known unsupported features;
- validation status;
- warnings requiring engineering review.

#### 8.2.6 Model repair suggestions

AI can propose fixes, but SAM should apply them only through controlled rules or explicit engineer approval.

Examples:

- assign missing default construction;
- fix missing schedule reference;
- detect orphan spaces;
- detect invalid surface orientation;
- detect unrealistic infiltration values;
- suggest grouping tiny zones;
- suggest adding design days.

The repair workflow should be explicit:

```text
Detect issue -> Propose fix -> Show impact -> Apply only if approved -> Re-run validation
```

#### 8.2.7 Prompt-to-study workflows

Enable natural-language study setup while keeping structured SAM settings underneath.

Example prompts:

```text
Run three glazing ratios: 30%, 40%, and 50%.
Compare annual cooling load and peak cooling.
Keep all other assumptions unchanged.
```

SAM should convert this to structured parametric instructions, not rely on free-form text during execution.

#### 8.2.8 Regression and change explanation

When a pull request changes model generation logic, AI can summarize:

- what changed in generated OpenStudio objects;
- which validation cases moved;
- whether changes are expected;
- which tests need review.

This is especially useful when comparing generated `.osm`, `.idf`, `.epJSON`, `.sql`, or result summaries.

### 8.3 Proposed technical design

#### 8.3.1 Structured first, AI second

Create machine-readable outputs first:

```text
OpenStudioTranslationReport.json
SimulationWarnings.json
SimulationResultsSummary.json
ValidationComparison.json
Assumptions.json
```

AI should consume these structured files and generate explanations.

This avoids asking AI to infer everything from raw simulation files.

#### 8.3.2 Deterministic validation layer

Before AI interpretation, SAM should run deterministic checks:

- missing weather;
- zero or negative area;
- unassigned construction;
- invalid schedule values;
- duplicated names;
- disconnected thermal zones;
- apertures without host surfaces;
- impossible infiltration values;
- suspicious glazing ratios;
- missing output requests.

#### 8.3.3 AI explanation layer

AI can then produce:

- engineering summary;
- warning digest;
- assumptions report;
- likely cause analysis;
- suggested next actions;
- comparison commentary.

#### 8.3.4 Human approval layer

Any automated model repair should require:

- list of proposed changes;
- affected SAM objects;
- previous value;
- proposed value;
- reason;
- expected impact;
- approval before application.

### 8.4 Suggested implementation work packages

#### WP4.1 - Translation report schema

Create a stable schema for all assumptions, warnings, skipped objects, approximations, and unsupported features.

Deliverables:

- `OpenStudioTranslationReport` object;
- JSON export;
- Markdown export;
- unit tests.

#### WP4.2 - Warning parser

Parse EnergyPlus/OpenStudio warnings and map them back to SAM/OpenStudio objects where possible.

Deliverables:

- warning parser;
- warning categories;
- severity classification;
- object-name matching strategy;
- example warning library.

#### WP4.3 - QA rules engine

Create deterministic QA checks before and after simulation.

Deliverables:

- pre-simulation checks;
- post-simulation checks;
- severity levels;
- developer-facing test cases;
- engineer-facing report output.

#### WP4.4 - AI report generator

Generate a readable report from structured QA data.

Deliverables:

- model summary report;
- assumptions report;
- warning explanation report;
- result interpretation report;
- validation comparison report.

#### WP4.5 - Safe model repair workflow

Allow AI-suggested repairs only when they can be represented as explicit structured changes.

Deliverables:

- proposed-change object model;
- approval workflow;
- before/after diff;
- revalidation trigger;
- audit trail.

#### WP4.6 - Pull request QA assistant

For repository changes, generate a simulation-impact summary.

Deliverables:

- changed test cases summary;
- generated model diff summary;
- result movement summary;
- suspected cause commentary;
- checklist for reviewer.

### 8.5 Example Phase 4 outputs

```text
/qa/model-summary.md
/qa/translation-report.json
/qa/translation-report.md
/qa/energyplus-warnings.json
/qa/energyplus-warnings.md
/qa/simulation-assumptions.md
/qa/validation-comparison.md
/qa/proposed-repairs.json
```

### 8.6 Acceptance criteria

- AI reports are generated only from structured SAM/OpenStudio/EnergyPlus outputs.
- Deterministic validation runs before AI interpretation.
- Unsupported features are never hidden.
- AI suggestions are labelled as suggestions, not facts.
- Any model repair requires explicit approval or a deterministic rule.
- Reports are useful to MEP engineers and avoid unnecessary developer jargon.
- The same input model produces repeatable structured QA outputs.

### 8.7 Risks and mitigations

| Risk | Mitigation |
|---|---|
| AI invents unsupported assumptions | AI consumes structured reports only; deterministic checks remain source of truth |
| Engineers over-trust AI explanations | Reports clearly separate measured result, deterministic warning, and suggested interpretation |
| Warning parsing is brittle | Maintain a warning library with regression tests |
| Model repair changes results unexpectedly | Use before/after diff, approval, and automatic revalidation |
| Reports become too verbose | Provide summary, detailed, and developer modes |

---

## 9. Phase 5 - Advanced HVAC, Controls, Modelica/FMI, and Digital Twin Readiness

### Goal

Prepare SAM for advanced simulation workflows beyond static annual loads and idealised HVAC, including detailed HVAC, dynamic controls, co-simulation, model predictive control, and digital twin workflows.

This phase should begin only after the core OpenStudio/EnergyPlus route and validation framework are stable.

### 9.1 Why this phase matters

Most early building energy workflows focus on geometry, fabric, internal gains, schedules, and ideal loads. That is enough for early design, benchmarking, and many comparative studies.

However, future workflows will increasingly need:

- control logic;
- plant behaviour;
- transient system response;
- operational calibration;
- sensor feedback;
- demand response;
- grid interaction;
- model predictive control;
- fault detection;
- digital twin integration.

These topics are difficult to represent cleanly in a simple static export. SAM should therefore separate **building simulation intent** from **control/system simulation intent**.

### 9.2 Strategic direction

Phase 5 should not try to implement everything directly inside SAM. Instead, SAM should become the coordinator that can prepare, connect, and validate models across specialist engines.

Recommended future structure:

```text
SAM AnalyticalModel
        |
        +--> OpenStudio/EnergyPlus envelope and zone loads
        |
        +--> Modelica/FMI system and control model
        |
        +--> BOPTEST-style control benchmark
        |
        +--> Operational data / digital twin feedback
        |
        v
SAM Results and QA
```

### 9.3 Candidate technology routes

#### 9.3.1 Detailed OpenStudio/EnergyPlus HVAC

Use OpenStudio/EnergyPlus for more detailed HVAC where it is sufficient and practical.

Candidate scope:

- air loops;
- plant loops;
- coils;
- heat pumps;
- boilers;
- chillers;
- heat recovery;
- terminal units;
- setpoint managers;
- sizing parameters;
- HVAC templates or standards-based generation.

This is likely the most practical next step after ideal loads.

#### 9.3.2 Modelica and FMI

Use Modelica/FMI where dynamic systems and controls are more important than conventional annual energy simulation.

Candidate scope:

- plant-room dynamics;
- control sequences;
- thermal storage;
- heat pump behaviour;
- district energy networks;
- supervisory control;
- co-simulation;
- model predictive control testing.

SAM should not need to become a Modelica authoring tool. It should provide enough structured information to generate or parameterise external system models.

#### 9.3.3 Spawn-style workflows

Monitor Spawn-style workflows where EnergyPlus envelope/zone simulation is coupled with Modelica-based HVAC and controls.

Potential value:

- keep EnergyPlus strengths for building envelope and loads;
- use Modelica strengths for system dynamics and controls;
- support co-simulation and control experiments.

#### 9.3.4 BOPTEST-style benchmarking

BOPTEST-style workflows are relevant for control testing and comparison.

Potential SAM use:

- prepare benchmark model metadata;
- define control inputs and outputs;
- run controller tests;
- compare control strategies;
- document reproducible control experiments.

#### 9.3.5 Operational data and digital twins

Longer term, SAM could help connect simulation models to measured data.

Candidate scope:

- sensor mapping;
- metered data mapping;
- weather normalisation;
- calibration inputs;
- fault detection context;
- performance drift reporting;
- comparison between simulated and measured behaviour.

### 9.4 Required SAM abstractions

Before implementing advanced engines, SAM should define additional abstractions.

#### 9.4.1 HVAC intent model

Describe HVAC intent without binding it immediately to a specific engine.

Possible concepts:

```text
System type
Served zones
Heating source
Cooling source
Ventilation strategy
Heat recovery
Terminal type
Control setpoints
Operating schedules
Sizing assumptions
Efficiency assumptions
```

#### 9.4.2 Control intent model

Describe controls as structured intent.

Possible concepts:

```text
Controlled variable
Sensor location
Actuator
Setpoint
Deadband
Schedule
Priority
Fallback condition
Control mode
```

#### 9.4.3 Co-simulation interface

Define how SAM would exchange information with external engines.

Possible concepts:

```text
Input variables
Output variables
Units
Time step
Warm-up period
Simulation horizon
Initial conditions
Error handling
Result synchronisation
```

#### 9.4.4 Operational data mapping

Define how measured data relates to SAM simulation objects.

Possible concepts:

```text
Sensor ID
Measured quantity
Unit
SAM object reference
Location
Aggregation interval
Data quality flag
Time zone
Source system
```

### 9.5 Suggested implementation work packages

#### WP5.1 - HVAC intent schema

Create a neutral HVAC intent schema that can later be translated into OpenStudio, EnergyPlus, Modelica, or other engines.

Deliverables:

- HVAC intent object model;
- supported system taxonomy;
- mapping to ideal loads;
- mapping to simple OpenStudio HVAC;
- unsupported feature reporting.

#### WP5.2 - Detailed OpenStudio HVAC prototype

Extend the OpenStudio adapter beyond ideal loads.

Candidate first systems:

- ideal loads air system with enhanced reporting;
- packaged single-zone system;
- simple variable air volume system;
- heat pump loop, if aligned with project needs;
- heat recovery ventilation, if aligned with project needs.

Deliverables:

- system selection options;
- generated OpenStudio HVAC objects;
- HVAC sizing settings;
- HVAC result extraction;
- validation cases.

#### WP5.3 - Control intent schema

Define how controls are represented in SAM before mapping to any specific simulation tool.

Deliverables:

- control intent object model;
- setpoint schedule model;
- sensor/actuator references;
- rule-based control examples;
- limitations report.

#### WP5.4 - FMI feasibility study

Create a small feasibility test for exchanging data with an FMI-compatible workflow.

Deliverables:

- candidate use case;
- input/output variable list;
- data exchange test;
- time-step sensitivity notes;
- recommendation on whether to continue.

#### WP5.5 - Modelica/Spawn feasibility study

Assess whether SAM should generate, parameterise, or simply reference Modelica-based models.

Deliverables:

- candidate system model;
- parameter mapping from SAM;
- result mapping back to SAM;
- comparison with EnergyPlus HVAC route;
- recommendation for next stage.

#### WP5.6 - BOPTEST-style controller benchmark

Investigate whether SAM can prepare model metadata and result comparison for controller benchmarking.

Deliverables:

- control benchmark use case;
- input/output mapping;
- baseline controller;
- comparison metrics;
- report template.

#### WP5.7 - Digital twin data mapping prototype

Prepare a minimal link between SAM model objects and measured operational data.

Deliverables:

- sensor-to-SAM mapping schema;
- measured vs simulated comparison output;
- data quality flags;
- calibration notes;
- drift report example.

### 9.6 Phase 5 validation metrics

Additional metrics beyond Phase 3:

- HVAC energy by end use;
- fan and pump energy;
- plant efficiency;
- heat recovery effectiveness;
- unmet hours by control mode;
- control stability;
- actuator movement/count;
- peak demand;
- demand response performance;
- measured vs simulated error;
- calibration error metrics, where relevant.

### 9.7 Acceptance criteria

- SAM can describe HVAC intent without locking the model to one engine.
- A simple detailed HVAC OpenStudio example can run and return results.
- Unsupported HVAC/control features are reported clearly.
- At least one co-simulation or FMI feasibility test is documented.
- Control inputs and outputs are represented with explicit units and time steps.
- Digital twin work remains separate from core design simulation until proven.
- Phase 5 features do not destabilise the simpler Phase 1 to 3 workflows.

### 9.8 Risks and mitigations

| Risk | Mitigation |
|---|---|
| Detailed HVAC scope becomes too large | Start with a small number of common systems and keep unsupported reporting explicit |
| Engine-specific HVAC logic pollutes SAM core | Keep HVAC intent neutral and place engine mapping inside adapters |
| Controls require dynamic simulation expertise | Treat Modelica/FMI/BOPTEST as specialist routes with feasibility gates |
| Co-simulation is hard to test | Begin with one small benchmark case and strict input/output definitions |
| Digital twin ambition grows too early | Keep operational-data mapping as a separate prototype until validated |
| Results from different engines diverge | Use documented assumptions, validation cases, and metric-specific tolerances |

---

## 10. Suggested Repository Structure

Possible structure for this repository as the roadmap matures:

```text
/docs
    SAM_Simulation_Engine_Roadmap.md
    OpenStudio_Translation_Assumptions.md
    OpenStudio_Validation_Methodology.md

/examples
    single-zone-box
    glazing-ratio-study
    multi-zone-model

/src
    SAM.OpenStudio
    SAM.OpenStudio.Grasshopper
    SAM.OpenStudio.Tests

/validation
    cases
    baselines
    reports
```

---

## 11. Near-Term Recommendations

Recommended next actions:

1. Keep TAS as the production route.
2. Build a minimal `SAM -> OpenStudio -> EnergyPlus -> SAM Results` path.
3. Limit the first scope to ideal loads and simple zone-level outputs.
4. Create translation reports from day one.
5. Create validation cases before adding detailed HVAC.
6. Add AI only after structured reports and deterministic checks exist.
7. Prepare HVAC/control abstractions before committing to Modelica/FMI.

---

## 12. Decision Log

| Decision | Recommendation | Reason |
|---|---|---|
| Replace TAS? | No | TAS remains fast and practical for current SAM workflows |
| Add OpenStudio/EnergyPlus? | Yes | Open, research-aligned, AI-friendly, validation-capable |
| Start with detailed HVAC? | No | Too much complexity before geometry, loads, and validation are stable |
| Use AI directly to modify models? | No | AI should propose; SAM should validate; engineers should approve |
| Prepare for Modelica/FMI? | Yes, later | Important for controls, MPC, digital twins, and co-simulation |

---

## 13. External References

Reference links to review while implementing this roadmap:

- OpenStudio website: <https://openstudio.net/>
- OpenStudio SDK repository: <https://github.com/NatLabRockies/OpenStudio>
- OpenStudio Measure Writer's Reference Guide: <https://nrel.github.io/OpenStudio-user-documentation/reference/measure_writing_guide/>
- EnergyPlus documentation: <https://energyplus.readthedocs.io/>
- EnergyPlus C API: <https://energyplus.readthedocs.io/en/latest/c.html>
- EnergyPlus Python API: <https://energyplus.readthedocs.io/en/latest/api.html>
- EnergyPlus epJSON schema: <https://energyplus.readthedocs.io/en/latest/schema.html>

---

## 14. Bottom Line

The strategic position should be:

> TAS remains SAM's fast production simulation route. OpenStudio/EnergyPlus becomes SAM's open, research-aligned, AI-friendly, validation-capable second engine. The real asset is not either engine; the real asset is SAM's neutral analytical model and simulation abstraction.

This gives SAM resilience: commercial reliability from TAS, academic credibility from EnergyPlus/OpenStudio, and a future route toward AI-assisted simulation, advanced controls, and digital-twin workflows.
