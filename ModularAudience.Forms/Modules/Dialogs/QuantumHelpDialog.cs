namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed class QuantumHelpDialog : Form
{
    internal static readonly (string Title, string Text)[] Topics =
    [
        ("Quick start", """
        QUANTUM BEAT ENGINE · QUICK START

        1. Select two or more samples in an Audio Collection.
        2. Right-click → Generate Breakbeat → Quantum Beat Engine (IBM®).
        3. Start with Local state-vector simulator. No account or Python is needed.
        4. In Circuit designer, load Bell break or Weighted groove. Change gates in
           the table, or choose H / X / CX / Ry and press Add gate.
        5. Sample tracks assigns a measured trigger qubit to each instrument.
        6. Configure tempo, bars, resolution and variations in Rhythm + DSP.
        7. Press Measure + Generate. The diagram and Note timings show the result.
        8. Play the rendered beat, or Add to collection for independent editing/export.

        Remap counts reuses the most recent measurements without another quantum
        job. It is ideal for exploring density, note lengths and DSP ranges.
        Changing the circuit requires a new measurement before remapping.

        Every window is modeless. Other editors, collections and playback remain
        usable while a connection, simulation, IBM job or audio render is running.
        """),
        ("Circuit gates and measurement", """
        CIRCUIT GATES AND MEASUREMENT

        H: Hadamard, creating superposition from a basis state.
        X: bit flip; X after a Bell-pair CX can create anti-correlated outcomes.
        CX: controlled NOT. Qubit is the control; CX target must be different.
        Ry: rotation about the Y axis. The editor uses degrees, QASM uses radians.
        From |0>, Ry(theta) gives P(1) = sin²(theta / 2).

        Operations run in table order. Move a gate up/down to change execution
        order; disable its On checkbox to keep it in the draft without executing it.
        Click a diagram wire to select a qubit; right-click to select a CX target.
        Clicking an operation column selects its gate-table row.

        Width is 4–16 logical qubits. Infer width uses the highest referenced gate,
        track and modifier qubit. Grid resolution does not require more qubits.
        Tracks can share bits, invert a trigger or shift their notes in time.
        H and CX only create entanglement for suitable input states; adding a CX
        does not by itself guarantee an entangled state.

        All logical qubits are measured into classical register c at the end.
        IBM/Qiskit ordering is used: q0 is the RIGHTMOST character in a bitstring.
        Shots is the number of repeated measurements, not the number of notes.
        More shots improve the measured distribution but do not lengthen the beat.

        The local state-vector simulator supports H, X, CX and numeric Ry gates.
        It models interference, not just independent random coin flips.
        """),
        ("Rhythm and DSP mapping", """
        RHYTHM AND DSP MAPPING

        One weighted measured state is chosen per grid step, shared by every track.
        This preserves measured correlations between the lanes' trigger qubits.
        A track triggers when its chosen bit is 1 (or 0 with Invert enabled).
        Post-map density and track density thin hits classically. Ghost-note
        probability is an explicitly classical addition. Interleaved keeps one
        active sample per step. Seed makes this mapping reproducible.

        Repeat first bar repeats one measured phrase. Alternate A / B repeats two
        bars; Independent bars gives each bar its own selections. Swing delays odd
        steps. The histogram does not preserve the original temporal order of shots.

        Enabled variations read adjacent pairs of measured bits, starting at the
        Modifier starting qubit and wrapping at the circuit width. Their integer
        values 0–3 interpolate within the configured ranges. Different modifiers
        may reuse bits, especially in small circuits; this intentionally creates
        correlated variations rather than inventing additional quantum information.

        Processing order: cut start → reverse → pitch-preserving pitch shift →
        time stretch → varispeed → optional fit / truncate → velocity → mix / gain.
        Time stretch changes duration without the intended pitch change.
        Varispeed changes both duration and pitch. Fit audio to note duration is
        an additional pitch-preserving length operation and overrides final length.
        Truncate limits note tails without stretching them. Keep end tails controls
        whether the final arrangement can extend past its last bar.

        Audio DSP is classical. There is no physical 'time-stretch gate' on a QPU.
        Quantum measurements drive parameters; the audio is rendered locally.
        Histograms alone cannot certify entanglement or recover quantum phase.
        """),
        ("IBM hardware setup", """
        IBM HARDWARE SETUP

        1. Obtain access to an IBM Quantum Platform instance and note its CRN.
        2. In Connection, enter an IBM Cloud API key (recommended) or IAM bearer
           token, the instance CRN, and the correct regional REST base URL.
        3. Test / Connect performs read-only backend discovery. It never submits
           a job. Select a backend and reconnect if the selected backend changes.
        4. Install Qiskit in a Python virtual environment you control, for example:

           python -m venv quantum-env
           quantum-env\Scripts\python.exe -m pip install qiskit

           On Linux/macOS, the executable is quantum-env/bin/python.
           This Windows application expects the local Windows executable path.
        5. Set Qiskit Python executable to that environment's python.exe.
        6. Choose IBM Quantum REST, then explicitly enable Allow IBM cloud jobs.
        7. Measure + Generate reads the backend's native gates/connectivity,
           invokes local Qiskit transpilation, and submits the ISA circuit via REST.

        The helper receives only circuit and backend-target metadata through stdin.
        Credentials are NOT passed to Python, the command line, or a script file.
        No dependencies are automatically installed. Python runs in isolated mode,
        so use an environment with Qiskit installed rather than relying on PYTHONPATH.

        Advanced: enable Use pasted ISA QASM and paste a pre-transpiled circuit in
        Hardware ISA QASM. It must match the selected backend and measure the same
        logical width into c. This is your responsibility; the REST API validates
        the actual backend ISA. Do not paste the uncompiled logical H/CX/Ry circuit.

        API keys are exchanged for short-lived IAM bearer tokens automatically.
        A directly supplied IAM token expires; re-enter it when necessary.
        Compilation can fail if a backend exposes unsupported or incomplete target
        metadata. Do not bypass hardware validation by relabelling logical QASM.
        """),
        ("Timing, costs and cancellation", """
        TIMING, COSTS AND CANCELLATION

        Response delay is the measured end-to-end execution wait, including network,
        authentication, IBM queue time and polling. It is not audio latency or the
        quantum device's gate time. The waiting label updates while an operation runs.
        Local Qiskit compilation time is reported separately after generation.

        IBM hardware jobs can consume instance allocation or incur charges.
        Permission is deliberately unchecked whenever a workspace/profile is loaded.
        Neither opening the editor nor restoring settings connects or submits jobs.
        Changing DSP settings and using Remap counts does not submit another job.

        HTTP timeouts, total job deadline, polling and read retries are configurable.
        GET requests may be retried on transient failures. Job-submission POSTs are
        NEVER automatically retried, to avoid duplicate billable jobs.

        Cancel stops this application's local wait/render/compilation. A submitted
        IBM job may still be queued or running. Inspect/cancel it in IBM Quantum
        Platform before resubmitting. A submission timeout can occur before a job ID
        is received. Disconnect and closing the editor do not cancel platform jobs.
        """),
        ("Saving, profiles and security", """
        SAVING, PROFILES AND SECURITY

        Settings live in %APPDATA%\ModularAudience\QuantumBeatEngine:

          workspace.json                       Current editor setup
          Experiments\experiment-<name>.json   Named setups
          Connections\connection-<name>.json  Encrypted connection profiles
          Recovery\workspace-<session>.json   Draft recovery after a corrupt file

        Edit → Save / Load / Rename manages named experiments. When a named setup
        is loaded, Delete removes the saved preset, not the samples or current draft.
        Without a named setup, Edit offers Reset current setup instead.
        The library window uses explicit replacement/deletion confirmation checkboxes.

        Auto-save is itself persistent. When checked, edits are saved after a short
        debounce; loaded named setups are also updated when valid. On normal close,
        the current workspace is saved even if Auto-save is unchecked. Draft gate
        tables can be restored; named experiments must pass circuit validation.

        Sample PCM is not stored. Mappings are reused only when the current sample
        names/order match; otherwise they reset for the new selection.
        API keys / tokens are excluded from workspace and experiment JSON.
        In connection JSON, ProtectedSecret is encrypted with Windows DPAPI for
        the current Windows user. It is not a portable plaintext token. Copying it
        to another Windows user or machine may prevent decryption. Keep backups
        secure and re-enter credentials if Windows cannot decrypt them.

        Connection settings are saved after a successful connection, on changes
        with auto-save, with Save protected, and on close. Forget saved profile
        deletes the protected profile and clears the credential field. Disconnect
        alone keeps the saved profile. Direct IAM tokens still expire on disk.

        Invalid/corrupt workspace files are preserved. Recovery uses a separate file
        until an explicit Reset. File writes use a temporary file and atomic move.
        No credentials or raw provider exception messages are displayed in errors.
        """),
        ("Troubleshooting", """
        TROUBLESHOOTING

        Generate disabled: add sample tracks; for IBM, connect and allow cloud jobs.
        Invalid circuit: check indices, CX control != target, and circuit width.
        No notes: inspect counts and trigger bits; increase post-map density, use H
        or weighted Ry rotations, disable Interleaved, or enable classical ghosts.
        Identical bars: choose Independent bars instead of Repeat first bar / A/B.
        Remap unavailable: measure first. A changed circuit needs new measurements.
        Silent audio: check track gains, minimum/maximum velocity, source PCM,
        truncation duration, and output gain. Local simulation can verify wiring.
        Cut beyond a sample: the start is clamped to its last complete audio frame.
        Authorization error: check API key vs IAM-token mode, expiry, instance CRN,
        IAM permissions and regional endpoint. Tokens are never used as API keys.
        Hardware rejection: use proper ISA QASM for the selected backend; reconnect
        after choosing a different backend and check its width / shot limits.
        Qiskit failure: use the correct virtual environment's python.exe, install
        qiskit there, and inspect Logical QASM plus the native backend gate set.
        Settings error: check AppData permissions and profile names. Do not erase
        corrupt settings blindly; the editor preserves them and supports recovery.

        Safety limits: 4–16 logical qubits, 512 gates, 32 sample tracks, 1,024 grid
        steps, 256 MiB combined source / prepared PCM and 256 MiB output PCM.
        Start with short drum one-shots before experimenting with long loops.
        """),
        ("About / trademarks", """
        ABOUT QUANTUM BEAT ENGINE

        ModularAudience experimental quantum-controlled rhythm and DSP workbench.
        Local simulation, logical OpenQASM 2/3 generation, IBM Quantum REST access,
        optional local Qiskit hardware transpilation, and classical audio rendering.

        IBM is a registered trademark of International Business Machines Corporation.
        The menu uses IBM® rather than a copyright symbol. This is an independent
        third-party integration, not an IBM product and not endorsed by IBM.

        Quantum randomness is not a guarantee of musical quality. Listen at a safe
        level, start locally, and enable paid hardware execution only when ready.
        """)
    ];

    private readonly ListBox topics = new() { Dock = DockStyle.Fill };
    private readonly TextBox content = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
        WordWrap = false, Font = new Font("Consolas", 10) };

    internal QuantumHelpDialog()
    {
        this.Name = "QuantumHelpDialog"; this.Text = "Quantum Beat Engine · Help"; this.ClientSize = new Size(1100, 680);
        this.MinimumSize = new Size(760, 450); this.StartPosition = FormStartPosition.CenterParent;
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1100, 640), SplitterDistance = 230 };
        this.topics.Items.AddRange(Topics.Select(topic => (object)topic.Title).ToArray());
        this.topics.SelectedIndexChanged += (_, _) => { if (this.topics.SelectedIndex >= 0) this.content.Text = Topics[this.topics.SelectedIndex].Text.Replace("\n", Environment.NewLine); };
        split.Panel1.Controls.Add(this.topics); split.Panel2.Controls.Add(this.content);
        var link = new LinkLabel { Text = "Open official IBM Quantum documentation", Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(10, 5, 5, 5) };
        link.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://quantum.cloud.ibm.com/docs/en/api/qiskit-runtime-rest") { UseShellExecute = true }); }
            catch { this.content.Text = "The browser could not be opened. Visit https://quantum.cloud.ibm.com/docs/en/api/qiskit-runtime-rest manually."; }
        };
        this.Controls.Add(split); this.Controls.Add(link); this.topics.SelectedIndex = 0;
    }
    internal void SelectTopic(string title) => this.topics.SelectedItem = title;

    internal static void ShowMessage(IWin32Window owner, string title, string message)
    {
        var window = new Form { Text = title, ClientSize = new Size(540, 180), StartPosition = FormStartPosition.CenterParent };
        window.Controls.Add(new TextBox { Text = message, Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Margin = new Padding(12) });
        var close = new Button { Text = "Close", Dock = DockStyle.Bottom, Height = 32 };
        close.Click += (_, _) => window.Close(); window.Controls.Add(close); window.Show(owner);
    }
}
