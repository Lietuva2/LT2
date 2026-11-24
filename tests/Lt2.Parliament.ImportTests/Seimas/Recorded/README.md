# Recorded Seimas responses

Real responses of the Seimas open-data service (`https://apps.lrs.lt/sip/`, CC BY 4.0: Lietuvos Respublikos Seimo
kanceliarija).

The live contract suite (`LiveContractTests`) fills this folder: run it once with `LT2_SEIMAS_LIVE=1` and it saves
every response it fetches here, byte for byte, under the same file names the synthetic responses use, then writes
`contract-report.md` with the verdict on every line of [`../ASSUMPTIONS.md`](../ASSUMPTIONS.md).

Nothing in this folder may be edited by hand or invented; invented responses live in [`../Synthetic/`](../Synthetic/).
