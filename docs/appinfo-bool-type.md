# Valve appinfo boolean type (0x14)

A snippet from the representative `appinfo.vdf` fixture used in integration tests highlights the new binary type emitted by Steam:

```
657874656e646564001449735375627363726962656446726f6d46616d696c7953686172696e670001
```

Breaking down the bytes:

- `657874656e64656400` &rarr; UTF-8 for the key name `extended` plus the null terminator.
- `14` &rarr; the binary type identifier (`0x14` / decimal `20`).
- `49735375627363726962656446726f6d46616d696c7953686172696e67` &rarr; UTF-8 for `IsSubscribedFromFamilySharing` plus the terminating `00`.
- `01` &rarr; single-byte payload indicating the boolean value `true` (zero would indicate `false`).

This confirms that type `0x14` represents a boolean stored as a one-byte payload, matching the expectations for the new appinfo encoding.
