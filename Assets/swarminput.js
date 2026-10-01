"use strict";
/**
 * Collects the SwarmUI generation input the enabled context channels need, with SwarmUI's own
 * primitives, and parses nothing: the server reads it with SwarmUI's parameter parser.
 *
 * AUTHORITATIVE SOURCE: Frontend/swarminput.ts. The committed Assets/swarminput.js is tsc build
 * output — do not hand-edit it.
 */
/** Reads the Generate tab's current Prompt Images and model-stack parameters. */
class PromptEnhanceSwarmInput {
    /** The Generate-tab parameter ids of the active model stack. */
    activeModelParams = ['model', 'loras', 'loraweights', 'loratencweights', 'lorasectionconfinement'];
    /** The `filedata` of every image in the Prompt Images area, in order: the selection SwarmUI's addPromptMediaToInput sends as `promptimages` (params.js). An attached image without data is an error, never skipped. */
    promptImages() {
        const area = getRequiredElementById('alt_prompt_image_area');
        const images = [...area.querySelectorAll('.alt-prompt-image')].filter((node) => node.tagName === 'IMG');
        return images.map((image, index) => {
            const { filedata } = image.dataset;
            if (!filedata) {
                throw new Error(`Image ${index + 1} is attached in SwarmUI but has no image data.`);
            }
            return filedata;
        });
    }
    /** The model-stack parameters SwarmUI's getGenInput would send: each registered, enabled one (`isParamEnabled`), read with `getInputVal(elem, true)` (params.js, util.js). */
    activeModel() {
        const input = {};
        for (const id of this.activeModelParams) {
            const param = gen_param_types.find((type) => type.id === id);
            if (param && isParamEnabled(param)) {
                const value = getInputVal(getRequiredElementById(`input_${id}`), true);
                if (value !== null) {
                    input[id] = value;
                }
            }
        }
        return input;
    }
    /** The `swarmInput` for the enabled channels, or null when neither Prompt Images nor the active model is sent. SwarmUI omits `promptimages` when none are attached; so does this. */
    collect(settings) {
        if (!(settings.sendPromptImages || settings.sendActiveModelContext)) {
            return null;
        }
        const input = settings.sendActiveModelContext ? this.activeModel() : {};
        const images = settings.sendPromptImages ? this.promptImages() : [];
        return images.length > 0 ? { ...input, promptimages: images } : input;
    }
}
/** Shared SwarmUI input collector. */
globalThis.promptEnhanceSwarmInput = new PromptEnhanceSwarmInput();
