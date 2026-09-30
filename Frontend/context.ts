/**
 * Canonical SwarmUI context collector: current PromptImages, recent generation attempts,
 * and active base-model/LoRA semantics. Provider-specific serialization stays server-side.
 */

class PromptEnhanceContext {
    history: PEGenerationAttempt[] = [];
    observer: MutationObserver | null = null;

    private parseDataImage(source: string, label: string): PEMediaEntry | null {
        let comma = source.indexOf(',');
        if (!source.startsWith('data:image/') || comma <= 0) {
            return null;
        }
        let header = source.substring(0, comma);
        let data = source.substring(comma + 1);
        if (!header.endsWith(';base64') || !data) {
            throw new Error(`${label} did not contain a valid base64 image.`);
        }
        return {
            type: 'base64',
            data,
            mediaType: header.substring('data:'.length, header.length - ';base64'.length),
            label
        };
    }

    private imageSource(source: string, label: string): Promise<PEMediaEntry> {
        let direct = this.parseDataImage(source, label);
        if (direct) {
            return Promise.resolve(direct);
        }
        return new Promise((resolve, reject) => {
            imageToData(source, (dataUrl) => {
                try {
                    let parsed = this.parseDataImage(dataUrl ?? '', label);
                    if (!parsed) {
                        reject(new Error(`${label} could not be loaded as an image.`));
                        return;
                    }
                    resolve(parsed);
                }
                catch (err) {
                    reject(err);
                }
            });
        });
    }

    async collectPromptImages(): Promise<PEMediaEntry[]> {
        let area = document.getElementById('alt_prompt_image_area');
        if (!area) {
            return [];
        }
        let nodes = [...area.querySelectorAll<HTMLElement>('.alt-prompt-image')]
            .filter((node) => node.tagName == 'IMG');
        let result: PEMediaEntry[] = [];
        for (let i = 0; i < nodes.length; i++) {
            let node = nodes[i]!;
            let label = `Image ${i + 1}`;
            let source = node.dataset.filedata || node.getAttribute('src') || '';
            if (!source) {
                throw new Error(`${label} is attached in SwarmUI but has no media source.`);
            }
            result.push(await this.imageSource(source, label));
        }
        return result;
    }

    private promptFromMetadata(raw: string): string {
        if (!raw) {
            return '';
        }
        try {
            let parsed = JSON.parse(raw) as {
                sui_image_params?: { prompt?: unknown };
                sui_extra_data?: { original_prompt?: unknown };
            };
            let original = parsed.sui_extra_data?.original_prompt;
            if (typeof original == 'string') {
                return original;
            }
            let prompt = parsed.sui_image_params?.prompt;
            return typeof prompt == 'string' ? prompt : '';
        }
        catch {
            return '';
        }
    }

    private batchIndex(block: HTMLElement): number {
        let batchId = block.dataset.batch_id ?? '';
        let suffix = batchId.includes('_') ? batchId.substring(batchId.lastIndexOf('_') + 1) : '';
        let parsed = Number.parseInt(suffix, 10);
        return Number.isFinite(parsed) ? parsed : Number.MAX_SAFE_INTEGER;
    }

    scanHistoryDom(): void {
        let batch = document.getElementById('current_image_batch');
        if (!batch) {
            return;
        }
        let blocks = [...batch.querySelectorAll<HTMLElement>('.image-block[data-request_id]')].reverse();
        for (let block of blocks) {
            if (block.dataset.is_generating == 'true' || block.dataset.is_placeholder == 'true') {
                continue;
            }
            let requestId = block.dataset.request_id ?? '';
            let src = block.dataset.src ?? '';
            if (!requestId || !src) {
                continue;
            }
            let metadata = block.dataset.metadata ?? '';
            let batchId = block.dataset.batch_id ?? `${requestId}_${this.batchIndex(block)}`;
            let attempt = this.history.find((entry) => entry.requestId == requestId);
            if (!attempt) {
                attempt = {
                    requestId,
                    prompt: this.promptFromMetadata(metadata),
                    outputs: []
                };
                this.history.push(attempt);
            }
            else if (!attempt.prompt) {
                attempt.prompt = this.promptFromMetadata(metadata);
            }
            if (!attempt.outputs.some((output) => output.batchId == batchId)) {
                attempt.outputs.push({
                    src,
                    metadata,
                    batchId,
                    batchIndex: this.batchIndex(block)
                });
                attempt.outputs.sort((a, b) => a.batchIndex - b.batchIndex);
            }
        }
    }

    observeHistory(): void {
        if (this.observer) {
            return;
        }
        let batch = document.getElementById('current_image_batch');
        if (!batch) {
            return;
        }
        this.scanHistoryDom();
        this.observer = new MutationObserver(() => this.scanHistoryDom());
        this.observer.observe(batch, { childList: true, subtree: true });
    }

    async collectPastGenerations(count: number): Promise<PEPastGenerationContext[]> {
        if (count <= 0) {
            return [];
        }
        this.scanHistoryDom();
        let selected = this.history.slice(-count);
        let result: PEPastGenerationContext[] = [];
        for (let generationIndex = 0; generationIndex < selected.length; generationIndex++) {
            let attempt = selected[generationIndex]!;
            let outputs: PEPastGenerationOutput[] = [];
            for (let outputIndex = 0; outputIndex < attempt.outputs.length; outputIndex++) {
                let output = attempt.outputs[outputIndex]!;
                let label = `Past Generation ${generationIndex + 1} Output ${outputIndex + 1}`;
                outputs.push({
                    image: await this.imageSource(output.src, label),
                    metadata: output.metadata
                });
            }
            result.push({
                requestId: attempt.requestId,
                prompt: attempt.prompt,
                outputs
            });
        }
        return result;
    }

    private describeModel(name: string, subtype: string): Promise<PEModelMetadataContext> {
        let fallback: PEModelMetadataContext = { name, tags: [] };
        return new Promise((resolve) => {
            genericRequest('DescribeModel', { modelName: name, subtype }, (data) => {
                if (!peIsRecord(data) || !peIsRecord(data.model)) {
                    resolve(fallback);
                    return;
                }
                let model = data.model;
                let tags: string[] = [];
                if (Array.isArray(model.tags)) {
                    tags = model.tags.filter((tag): tag is string => typeof tag == 'string');
                }
                else if (typeof model.tags == 'string' && model.tags) {
                    tags = model.tags.split(',').map((tag) => tag.trim()).filter((tag) => tag.length > 0);
                }
                resolve({
                    name: typeof model.name == 'string' && model.name ? model.name : name,
                    title: typeof model.title == 'string' ? model.title : undefined,
                    architecture: typeof model.architecture == 'string' ? model.architecture : undefined,
                    class: typeof model.class == 'string' ? model.class : undefined,
                    compatClass: typeof model.compat_class == 'string' ? model.compat_class : undefined,
                    description: typeof model.description == 'string' ? model.description : undefined,
                    usageHint: typeof model.usage_hint == 'string' ? model.usage_hint : undefined,
                    triggerPhrase: typeof model.trigger_phrase == 'string' ? model.trigger_phrase : undefined,
                    tags
                });
            }, 0, () => resolve(fallback));
        });
    }

    private csvNumbers(id: string): number[] {
        let elem = document.getElementById(id) as HTMLInputElement | null;
        if (!elem || !elem.value.trim()) {
            return [];
        }
        return elem.value.split(',').map((value) => Number.parseFloat(value.trim()));
    }

    private csvIntegers(id: string): number[] {
        let elem = document.getElementById(id) as HTMLInputElement | null;
        if (!elem || !elem.value.trim()) {
            return [];
        }
        return elem.value.split(',').map((value) => Number.parseInt(value.trim(), 10));
    }

    async collectActiveModelContext(): Promise<PEActiveModelContext> {
        let baseInput = (document.getElementById('input_model') || document.getElementById('current_model')) as HTMLSelectElement | HTMLInputElement | null;
        let baseName = baseInput?.value ?? '';
        let baseModel = baseName ? await this.describeModel(baseName, 'Stable-Diffusion') : null;

        let loraInput = document.getElementById('input_loras') as HTMLSelectElement | null;
        let names = loraInput ? [...loraInput.selectedOptions].map((option) => option.value) : [];
        let weights = this.csvNumbers('input_loraweights');
        let tencWeights = this.csvNumbers('input_loratencweights');
        let confinements = this.csvIntegers('input_lorasectionconfinement');
        let scopeNames: Record<number, string> = {
            0: 'Global',
            5: 'Base',
            1: 'Refiner',
            2: 'Video',
            3: 'VideoSwap'
        };
        let loras: PEActiveLoraContext[] = [];
        for (let i = 0; i < names.length; i++) {
            let metadata = await this.describeModel(names[i]!, 'LoRA');
            let weight = Number.isFinite(weights[i]) ? weights[i]! : 1;
            let textEncoderWeight = Number.isFinite(tencWeights[i]) ? tencWeights[i]! : weight;
            let scopeId = Number.isFinite(confinements[i]) ? confinements[i]! : 0;
            loras.push({
                ...metadata,
                weight,
                textEncoderWeight,
                scopeId,
                scope: scopeNames[scopeId] ?? `Section ${scopeId}`
            });
        }
        return { baseModel, loras };
    }

    start(): void {
        this.observeHistory();
    }
}

let promptEnhanceContext = new PromptEnhanceContext();
