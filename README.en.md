# Visual Terrain Block

[中文](README.md)

A Timberborn mod that adds a terrain block you can build without support underneath. It matches the vanilla terrain block in look, cost, and science, but it is not stopped by the usual support rules. Beavers build it like a normal construction site, and the finished block becomes real terrain.

## What it does

- Adds **Floating Terrain Block** to the **Landscaping** tool group.
- Extend it from a cliff face, or place it in the air. It does not wait for the block behind or below to finish.
- Costs **4 dirt** and **1000** science, the same as the vanilla terrain block.
- When beavers finish the site, the block is removed and that cell becomes real terrain.
- The resulting terrain can be dug, holds back water, accepts trees, and can be built on.
- Unfinished sites can be cancelled with the deletion tool. The dirt spent on construction is not refunded when the block finishes.

Finished terrain still follows the vanilla support rules. If dynamite or digging removes its support, the unsupported terrain beyond it collapses.

## Requirements

- Game version **1.1.0.0** or newer
- [Harmony](https://steamcommunity.com/workshop/filedetails/?id=3284904751) **2.4.1** or newer

## Further reading

See [docs/en.md](docs/en.md) for the design and implementation notes.
