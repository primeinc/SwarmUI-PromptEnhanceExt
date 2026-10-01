/**
 * Cross-file globals the PromptEnhance scripts assign on `globalThis` for one another, each in the
 * file named beside it. The scripts are classic scripts in one shared global scope, loaded in
 * PromptEnhanceExtension.OnPreInit order; a file reads only globals assigned by files loaded before
 * it, or reads them later from a callback.
 */

// contracts.ts
declare var PE_ROUTES: PERoutes;
declare var PE_API_KEY_TYPE: string;
declare var PE_DEFAULT_SETTINGS: PESettings;
declare var peErrorText: (err: unknown) => string;
declare var peNormalizeSettings: (raw: PERawSettingsInput, current: PESettings) => PESettings;
declare var peAdaptSettingsResult: (data: unknown) => PESettingsResult;
declare var peAdaptModelsResult: (data: unknown) => PEModelsResult;
declare var peAdaptEnhanceResult: (data: unknown) => PEEnhanceResult;

// settings.ts
declare var promptEnhanceSettings: PromptEnhanceSettings;

// swarminput.ts
declare var promptEnhanceSwarmInput: PromptEnhanceSwarmInput;
