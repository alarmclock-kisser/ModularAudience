"""Local ISA compilation only. Credentials and network access are not needed by this helper."""
import json
import sys

try:
    from qiskit import qasm2, qasm3, transpile
    from qiskit.circuit import Measure
    from qiskit.circuit.library import get_standard_gate_name_mapping
    from qiskit.transpiler import Target
except ImportError:
    print(json.dumps({"error": "qiskit_missing"}))
    sys.exit(0)

try:
    request = json.load(sys.stdin)
    config = request["Target"]
    width = int(config["QubitCount"])
    target = Target(num_qubits=width)
    standard = get_standard_gate_name_mapping()
    gate_maps = {gate["Name"]: gate["Qubits"] for gate in config["Gates"]}
    for name in config["BasisGates"]:
        if name not in standard or name in ("measure", "reset", "delay"):
            continue
        instruction = standard[name]
        if instruction.num_qubits not in (1, 2):
            continue
        locations = gate_maps.get(name)
        if not locations:
            locations = [[q] for q in range(width)] if instruction.num_qubits == 1 else config["CouplingMap"]
        properties = {tuple(q): None for q in locations if len(q) == instruction.num_qubits}
        if properties:
            target.add_instruction(instruction, properties)
    target.add_instruction(Measure(), {(q,): None for q in range(width)})
    logical = qasm2.loads(request["Qasm"])
    if logical.num_qubits != request["LogicalQubits"] or logical.num_clbits != request["LogicalQubits"]:
        raise ValueError("measurement_width")
    isa = transpile(logical, target=target, optimization_level=int(request["OptimizationLevel"]),
                    seed_transpiler=int(request["Seed"]))
    for item in isa.data:
        qubits = tuple(isa.find_bit(q).index for q in item.qubits)
        if item.operation.name != "barrier" and not target.instruction_supported(item.operation.name, qubits):
            raise ValueError("isa_validation")
    print(json.dumps({"qasm": qasm3.dumps(isa)}))
except Exception:
    # Do not echo arbitrary third-party exception messages or request contents.
    print(json.dumps({"error": "transpilation_failed"}))
