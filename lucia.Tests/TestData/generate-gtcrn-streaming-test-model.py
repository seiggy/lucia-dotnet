"""Generate the synthetic GTCRN-shaped ONNX test fixture.

This graph was authored for Lucia's tests and contains no external model
topology or weights. It is distributed under the repository's MIT license.
Only the Python standard library is required.
"""

import struct
import sys
from pathlib import Path


def varint(value: int) -> bytes:
    encoded = bytearray()
    while value > 0x7F:
        encoded.append((value & 0x7F) | 0x80)
        value >>= 7
    encoded.append(value)
    return bytes(encoded)


def field(number: int, wire_type: int, value: bytes) -> bytes:
    return varint((number << 3) | wire_type) + value


def integer(number: int, value: int) -> bytes:
    return field(number, 0, varint(value))


def text(number: int, value: str) -> bytes:
    encoded = value.encode("utf-8")
    return field(number, 2, varint(len(encoded)) + encoded)


def message(number: int, value: bytes) -> bytes:
    return field(number, 2, varint(len(value)) + value)


def packed_integers(number: int, values: tuple[int, ...]) -> bytes:
    encoded = b"".join(varint(value) for value in values)
    return field(number, 2, varint(len(encoded)) + encoded)


def packed_floats(number: int, values: tuple[float, ...]) -> bytes:
    encoded = struct.pack(f"<{len(values)}f", *values)
    return field(number, 2, varint(len(encoded)) + encoded)


def tensor(name: str, value: float) -> bytes:
    return integer(2, 1) + packed_floats(4, (value,)) + text(8, name)


def tensor_type(shape: tuple[int, ...]) -> bytes:
    dimensions = b"".join(message(1, integer(1, size)) for size in shape)
    return message(1, integer(1, 1) + message(2, dimensions))


def value_info(name: str, shape: tuple[int, ...]) -> bytes:
    return text(1, name) + message(2, tensor_type(shape))


def node(
    inputs: tuple[str, ...],
    output: str,
    operation: str,
    attributes: tuple[bytes, ...] = (),
) -> bytes:
    return (
        b"".join(text(1, value) for value in inputs)
        + text(2, output)
        + text(4, operation)
        + b"".join(message(5, attribute) for attribute in attributes)
    )


def keep_dimensions(value: int) -> bytes:
    return text(1, "keepdims") + integer(3, value) + integer(20, 2)


def build_model() -> bytes:
    mix_shape = (1, 257, 1, 2)
    conv_shape = (2, 1, 16, 16, 33)
    tra_shape = (2, 3, 1, 1, 16)
    inter_shape = (2, 1, 33, 16)
    nodes = (
        node(("conv_cache", "conv_delta"), "conv_cache_out", "Add"),
        node(("tra_cache", "tra_delta"), "tra_cache_out", "Add"),
        node(("inter_cache", "inter_delta"), "inter_cache_out", "Add"),
        node(("conv_cache",), "conv_mean", "ReduceMean", (keep_dimensions(0),)),
        node(("tra_cache",), "tra_mean", "ReduceMean", (keep_dimensions(0),)),
        node(("inter_cache",), "inter_mean", "ReduceMean", (keep_dimensions(0),)),
        node(("conv_mean", "tra_mean"), "cache_mean_1", "Add"),
        node(("cache_mean_1", "inter_mean"), "cache_mean", "Add"),
        node(("mix", "cache_mean"), "enh", "Add"),
    )
    initializers = (
        tensor("conv_delta", 0.01),
        tensor("tra_delta", 0.02),
        tensor("inter_delta", 0.03),
    )
    inputs = (
        value_info("mix", mix_shape),
        value_info("conv_cache", conv_shape),
        value_info("tra_cache", tra_shape),
        value_info("inter_cache", inter_shape),
    )
    outputs = (
        value_info("enh", mix_shape),
        value_info("conv_cache_out", conv_shape),
        value_info("tra_cache_out", tra_shape),
        value_info("inter_cache_out", inter_shape),
    )
    graph = (
        b"".join(message(1, value) for value in nodes)
        + text(2, "lucia-gtcrn-streaming-test")
        + b"".join(message(5, value) for value in initializers)
        + b"".join(message(11, value) for value in inputs)
        + b"".join(message(12, value) for value in outputs)
    )
    opset = text(1, "") + integer(2, 13)
    return integer(1, 8) + message(7, graph) + message(8, opset)


output_path = (
    Path(sys.argv[1])
    if len(sys.argv) > 1
    else Path(__file__).with_name("gtcrn-streaming-test.onnx")
)
output_path.write_bytes(build_model())
