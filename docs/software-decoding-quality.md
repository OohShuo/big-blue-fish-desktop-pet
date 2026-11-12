# Software decoding quality

Add a persisted SoftwareHighDefinition boolean, default false for existing users.
Settings: 软件解码画质 with 低清（640×360，24 帧） and 高清（1280×720，60 帧）.
Changes apply at the next animation, including hardware-decoder failure fallback.
Hardware decoding continues using the enhanced source. Do not interrupt a current
clip or change low-power scheduling. Verify preference round-trip and both real
software-decoding pipelines, including alpha, dimensions and seek.
