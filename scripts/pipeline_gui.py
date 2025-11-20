"""
Traffic Simulation Pipeline GUI
================================

A Streamlit interface for creating, managing, and running pipeline configurations.

Usage:
    streamlit run scripts/pipeline_gui.py

Features:
    - Quick file selection - automatically finds files in project!
    - Load and modify presets
    - Real-time validation
    - Live pipeline execution with logs

File Selection:
    📁 Select - Shows dropdown of matching files in project
    - Can also type paths manually
    - Searches up to 3 levels deep in project folder

Customization:
    1. Add presets: Edit PRESETS dictionary below (line ~35)
    2. Change defaults: Edit DEFAULTS dictionary (line ~41)
    3. Modify help text: Edit FORMAT_HELP (line ~56)

Author: Noah Kim
Date: 2025
"""

import streamlit as st
import yaml
from pathlib import Path
import subprocess
import sys
import os

# Add project root to path
PROJECT_ROOT = Path(__file__).parent.parent
sys.path.insert(0, str(PROJECT_ROOT))

# Import pipeline modules
from traffic_sim_module.pipeline.main_pipeline import PipelineConfig

# ============================================================================
# CONFIGURATION - Edit these to customize the GUI
# ============================================================================
config_files = list((PROJECT_ROOT / "configs").glob("*.yaml"))
# Available presets (add your own here!)
PRESETS = {
    "v4_test": "configs/v4_default.yaml",
    "v4_snapshots": "configs/v4_snapshots.yaml",
}

# Default values for new configs
DEFAULTS = {
    "xml_input": "data/raw/events_v4.xml",
    "gpkg_network": "data/raw/road_network_v4_clipped_single.gpkg",
    "parquet_intermediate": "data/interim/filtered_events.parquet",
    "output_base": "data/processed/trajectories_full",
    "start_time": "12:00",
    "end_time": "15:00",
    "frequency_seconds": 300,  # 5 minutes
    "duration_seconds": 5,
    "num_workers": 6,
    "chunk_size": 50000,
    "output_formats": ["geojson", "csv", "geoparquet"],
    "heatmap_enabled": False,
    "heatmap_time_interval": 300,  # 5 minutes
    "heatmap_output_formats": ["csv"],
    "heatmap_output_base": "data/processed/heatmap",
}

# Output format descriptions (for tooltips)
FORMAT_HELP = {
    "geojson": "Standard format for web visualization (large files)",
    "csv": "Simple format for Excel/spreadsheets (smallest files)",
    "parquet": "Columnar format for data analysis",
    "geoparquet": "Spatial parquet format - best for ArcGIS!"
}

# ============================================================================
# Helper Functions
# ============================================================================

def find_files(directory: Path, extensions: list, max_depth=3) -> list:
    """
    Recursively find files with given extensions.

    Args:
        directory: Root directory to search
        extensions: List of extensions like ['.xml', '.gz']
        max_depth: Maximum recursion depth

    Returns:
        List of file paths relative to PROJECT_ROOT
    """
    files = []

    def search(current_dir, depth):
        if depth > max_depth:
            return
        try:
            for item in current_dir.iterdir():
                if item.is_file():
                    if any(str(item).endswith(ext) for ext in extensions):
                        # Make path relative to PROJECT_ROOT
                        rel_path = item.relative_to(PROJECT_ROOT)
                        files.append(str(rel_path))
                elif item.is_dir() and not item.name.startswith('.'):
                    search(item, depth + 1)
        except PermissionError:
            pass

    search(directory, 0)
    return sorted(files)



def file_browser_widget(label: str, extensions: list, default_value: str = "", key: str = ""):
    """
    Create a file browser widget using Streamlit native components.

    Args:
        label: Widget label
        extensions: List of file extensions to filter
        default_value: Default file path
        key: Unique key for the widget

    Returns:
        Selected file path
    """
    col1, col2 = st.columns([5, 1])

    with col1:
        # Text input for manual entry
        path = st.text_input(
            label,
            value=default_value,
            help="Type path manually or use Quick Select",
            key=f"{key}_text"
        )

    with col2:
        st.write("")  # Spacing
        st.write("")  # Spacing
        # Button to open quick select
        if st.button("📁 Select", key=f"{key}_browse"):
            st.session_state[f"{key}_show_browser"] = True

    # Show file browser if button was clicked
    if st.session_state.get(f"{key}_show_browser", False):
        with st.expander("🔍 Quick Select", expanded=True):
            # Find files with matching extensions
            files = find_files(PROJECT_ROOT, extensions)

            if files:
                selected = st.selectbox(
                    "Select from available files:",
                    [""] + files,
                    key=f"{key}_selectbox"
                )

                col_a, col_b = st.columns(2)
                with col_a:
                    if st.button("✅ Use Selected", key=f"{key}_use"):
                        if selected:
                            st.session_state[f"{key}_value"] = selected
                            st.session_state[f"{key}_show_browser"] = False
                            st.rerun()
                with col_b:
                    if st.button("❌ Cancel", key=f"{key}_cancel"):
                        st.session_state[f"{key}_show_browser"] = False
                        st.rerun()
            else:
                st.warning(f"No files found with extensions: {', '.join(extensions)}")
                if st.button("❌ Close", key=f"{key}_close"):
                    st.session_state[f"{key}_show_browser"] = False
                    st.rerun()

    # Return the stored value or current input
    return st.session_state.get(f"{key}_value", path)


def load_preset(preset_path: str) -> dict:
    """Load a preset config file."""
    with open(PROJECT_ROOT / preset_path, 'r') as f:
        return yaml.safe_load(f)


def save_config(config_dict: dict, save_path: str):
    """Save config to YAML file."""
    with open(PROJECT_ROOT / save_path, 'w') as f:
        yaml.dump(config_dict, f, default_flow_style=False, sort_keys=False)


def validate_config(config_dict: dict) -> tuple[bool, str]:
    """Validate config using Pydantic model."""
    try:
        PipelineConfig(**config_dict)
        return True, "✅ Configuration is valid!"
    except Exception as e:
        return False, f"❌ Validation error: {str(e)}"


def run_pipeline(config_path: str):
    """Run the pipeline with the given config."""
    cmd = [
        sys.executable, "-m",
        "traffic_sim_module.pipeline.main_pipeline",
        str(config_path)
    ]

    # Run in project root
    process = subprocess.Popen(
        cmd,
        cwd=str(PROJECT_ROOT),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        universal_newlines=True,
        bufsize=1
    )

    return process


# ============================================================================
# Streamlit App
# ============================================================================

def main():
    st.set_page_config(
        page_title="Traffic Simulation Pipeline",
        page_icon="🚗",
        layout="wide"
    )

    st.title("🚗 Traffic Simulation Pipeline")
    st.markdown("Create, manage, and run pipeline configurations")

    # Sidebar - Mode selection
    st.sidebar.header("Mode")
    mode = st.sidebar.radio(
        "Select mode:",
        ["Create/Edit Config", "Run Pipeline", "Manage Presets"]
    )

    # ========================================================================
    # MODE 1: Create/Edit Config
    # ========================================================================
    if mode == "Create/Edit Config":
        st.header("⚙️ Configuration Builder")

        # Load preset or start fresh - show all existing configs
        col1, col2 = st.columns([3, 1])
        with col1:
            # Get all config files
            config_files = sorted([f.name for f in (PROJECT_ROOT / "configs").glob("*.yaml")])

            load_preset_option = st.selectbox(
                "Load existing config:",
                ["Start fresh"] + config_files
            )

        # Track loaded config name for save field
        loaded_config_name = None

        # Check if preset changed - if so, clear session state for path fields
        if "last_loaded_preset" not in st.session_state:
            st.session_state["last_loaded_preset"] = None

        if load_preset_option != st.session_state["last_loaded_preset"]:
            # Clear path field session state when changing presets
            for key in ["xml_input", "gpkg_network", "parquet_intermediate", "output_base"]:
                if key in st.session_state:
                    del st.session_state[key]
            st.session_state["last_loaded_preset"] = load_preset_option

        if load_preset_option != "Start fresh":
            config_dict = load_preset(f"configs/{load_preset_option}")
            loaded_config_name = load_preset_option
            st.success(f"Loaded config: {load_preset_option}")
        else:
            config_dict = {
                "paths": {},
                "filters": {},
                "processing": {},
                "skip_xml_to_parquet": False,
                "skip_parquet_to_geojson": False
            }

        st.divider()

        # Paths Section
        st.subheader("📁 File Paths")

        xml_input = st.text_input(
            "XML Input",
            value=config_dict.get("paths", {}).get("xml_input", DEFAULTS["xml_input"]),
            help="Path to XML events file",
            key="xml_input"
        )

        gpkg_network = st.text_input(
            "Network GeoPackage",
            value=config_dict.get("paths", {}).get("gpkg_network", DEFAULTS["gpkg_network"]),
            help="Path to road network GeoPackage file",
            key="gpkg_network"
        )

        parquet_intermediate = st.text_input(
            "Parquet Intermediate",
            value=config_dict.get("paths", {}).get("parquet_intermediate", DEFAULTS["parquet_intermediate"]),
            help="Path for intermediate parquet file",
            key="parquet_intermediate"
        )

        output_base = st.text_input(
            "Output Base Path",
            value=config_dict.get("paths", {}).get("output_base", DEFAULTS["output_base"]),
            help="Base path for outputs (without extension)",
            key="output_base"
        )

        st.divider()

        # Filters Section
        st.subheader("🕒 Snapshot Filters")
        col1, col2, col3, col4 = st.columns(4)

        with col1:
            start_time = st.text_input(
                "Start Time (HH:MM)",
                value=config_dict.get("filters", {}).get("start_time", DEFAULTS["start_time"]),
                help="Start of sampling period"
            )

        with col2:
            end_time = st.text_input(
                "End Time (HH:MM)",
                value=config_dict.get("filters", {}).get("end_time", DEFAULTS["end_time"]),
                help="End of sampling period"
            )

        with col3:
            frequency_seconds = st.number_input(
                "Frequency (seconds)",
                min_value=1,
                value=config_dict.get("filters", {}).get("frequency_seconds", DEFAULTS["frequency_seconds"]),
                help="Time between snapshots"
            )

        with col4:
            duration_seconds = st.number_input(
                "Duration (seconds)",
                min_value=1,
                value=config_dict.get("filters", {}).get("duration_seconds", DEFAULTS["duration_seconds"]),
                help="Duration of each snapshot"
            )

        # Calculate number of snapshots
        try:
            h_start, m_start = map(int, start_time.split(':'))
            h_end, m_end = map(int, end_time.split(':'))
            start_sec = h_start * 3600 + m_start * 60
            end_sec = h_end * 3600 + m_end * 60
            num_snapshots = (end_sec - start_sec) // frequency_seconds
            st.info(f"ℹ️ This will generate approximately **{num_snapshots}** snapshots")
        except:
            st.warning("⚠️ Invalid time format")

        st.divider()

        # Processing Section
        st.subheader("⚡ Processing Options")
        col1, col2 = st.columns(2)

        num_available_workers = os.cpu_count()


        with col1:
            num_workers = st.number_input(
                f"Number of Workers. Available: {num_available_workers} ",
                min_value=1,
                max_value=32,
                value=config_dict.get("processing", {}).get("num_workers", DEFAULTS["num_workers"]),
                help="CPU cores to use"
            )

            chunk_size = st.number_input(
                "Chunk Size",
                min_value=1000,
                max_value=1000000,
                value=config_dict.get("processing", {}).get("chunk_size", DEFAULTS["chunk_size"]),
                help="Events per chunk (larger = more memory)"
            )

        with col2:
            st.write("**Output Formats**")
            output_formats = []
            for fmt in ["geojson", "csv", "parquet", "geoparquet"]:
                default_checked = fmt in config_dict.get("processing", {}).get("output_formats", DEFAULTS["output_formats"])
                if st.checkbox(fmt.upper(), value=default_checked, help=FORMAT_HELP[fmt]):
                    output_formats.append(fmt)

        st.divider()

        # Heatmap Export Section
        st.subheader("🗺️ Heatmap Export (Optional)")

        heatmap_enabled = st.checkbox(
            "Enable Heatmap Export",
            value=config_dict.get("processing", {}).get("heatmap_enabled", DEFAULTS["heatmap_enabled"]),
            help="Generate time-sampled heatmap data with vehicle counts per link"
        )

        if heatmap_enabled:
            col1, col2 = st.columns(2)

            with col1:
                heatmap_time_interval = st.number_input(
                    "Time Interval (seconds)",
                    min_value=60,
                    max_value=3600,
                    value=config_dict.get("processing", {}).get("heatmap_time_interval", DEFAULTS["heatmap_time_interval"]),
                    help="Sampling interval (e.g., 300 = 5 minutes)"
                )

                heatmap_output_base = st.text_input(
                    "Heatmap Output Base Path",
                    value=config_dict.get("processing", {}).get("heatmap_output_base", DEFAULTS["heatmap_output_base"]),
                    help="Base path for heatmap outputs (without extension)"
                )

            with col2:
                st.write("**Heatmap Output Formats**")
                heatmap_output_formats = []
                for fmt in ["geojson", "csv", "parquet", "geoparquet"]:
                    default_checked = fmt in config_dict.get("processing", {}).get("heatmap_output_formats", DEFAULTS["heatmap_output_formats"])
                    if st.checkbox(f"Heatmap {fmt.upper()}", value=default_checked, help=FORMAT_HELP[fmt], key=f"heatmap_{fmt}"):
                        heatmap_output_formats.append(fmt)

            # Calculate number of heatmap timepoints
            try:
                h_start, m_start = map(int, start_time.split(':'))
                h_end, m_end = map(int, end_time.split(':'))
                start_sec = h_start * 3600 + m_start * 60
                end_sec = h_end * 3600 + m_end * 60
                num_timepoints = (end_sec - start_sec) // heatmap_time_interval + 1
                st.info(f"ℹ️ Heatmap will generate approximately **{num_timepoints}** timepoints at {heatmap_time_interval}s intervals")
            except:
                pass
        else:
            heatmap_time_interval = DEFAULTS["heatmap_time_interval"]
            heatmap_output_formats = DEFAULTS["heatmap_output_formats"]
            heatmap_output_base = DEFAULTS["heatmap_output_base"]

        st.divider()

        # Skip options
        st.subheader("⏭️ Skip Options")
        col1, col2 = st.columns(2)

        with col1:
            skip_xml = st.checkbox(
                "Skip XML → Parquet",
                value=config_dict.get("skip_xml_to_parquet", False),
                help="Skip if parquet already exists"
            )

        with col2:
            skip_geojson = st.checkbox(
                "Skip Parquet → Output",
                value=config_dict.get("skip_parquet_to_geojson", False),
                help="Skip if outputs already exist"
            )

        st.divider()

        # Build config dict
        new_config = {
            "paths": {
                "xml_input": xml_input,
                "gpkg_network": gpkg_network,
                "parquet_intermediate": parquet_intermediate,
                "output_base": output_base
            },
            "filters": {
                "start_time": start_time,
                "end_time": end_time,
                "frequency_seconds": int(frequency_seconds),
                "duration_seconds": int(duration_seconds)
            },
            "processing": {
                "num_workers": int(num_workers),
                "chunk_size": int(chunk_size),
                "output_formats": output_formats,
                "heatmap_enabled": heatmap_enabled,
                "heatmap_time_interval": int(heatmap_time_interval),
                "heatmap_output_formats": heatmap_output_formats,
                "heatmap_output_base": heatmap_output_base
            },
            "skip_xml_to_parquet": skip_xml,
            "skip_parquet_to_geojson": skip_geojson
        }

        # Validation
        is_valid, msg = validate_config(new_config)
        if is_valid:
            st.success(msg)
        else:
            st.error(msg)

        # Save section
        st.divider()
        st.subheader("💾 Save Configuration")

        col1, col2 = st.columns([3, 1])
        with col1:
            # Pre-fill with loaded config name if editing an existing config
            default_save_name = loaded_config_name if loaded_config_name else "my_config.yaml"

            save_name = st.text_input(
                "Config name",
                value=default_save_name,
                help="Filename (will be saved in configs/) - use same name to overwrite"
            )

        with col2:
            st.write("")  # Spacing
            st.write("")  # Spacing
            if st.button("💾 Save Config", type="primary", disabled=not is_valid):
                save_path = f"configs/{save_name}"
                save_config(new_config, save_path)
                st.success(f"✅ Saved to {save_path}")
                # Show overwrite message if it existed
                if save_name in config_files:
                    st.info("📝 Overwrote existing config")

        # Preview
        with st.expander("📄 Preview YAML"):
            st.code(yaml.dump(new_config, default_flow_style=False, sort_keys=False), language="yaml")

    # ========================================================================
    # MODE 2: Run Pipeline
    # ========================================================================
    elif mode == "Run Pipeline":
        st.header("▶️ Run Pipeline")

        # Select config
        config_files = list((PROJECT_ROOT / "configs").glob("*.yaml"))
        config_names = [f.name for f in config_files]

        selected_config = st.selectbox(
            "Select configuration:",
            config_names
        )

        if selected_config:
            config_path = PROJECT_ROOT / "configs" / selected_config

            # Load and display config
            with open(config_path, 'r') as f:
                config_dict = yaml.safe_load(f)

            with st.expander("📄 View Configuration"):
                st.code(yaml.dump(config_dict, default_flow_style=False), language="yaml")

            # Validate
            is_valid, msg = validate_config(config_dict)
            if is_valid:
                st.success(msg)
            else:
                st.error(msg)
                st.stop()

            st.divider()

            # Run button
            if st.button("▶️ Run Pipeline", type="primary"):
                st.info("🚀 Starting pipeline...")

                # Create log area
                log_area = st.empty()

                # Run pipeline
                process = run_pipeline(config_path)

                # Stream output
                logs = []
                for line in process.stdout:
                    logs.append(line)
                    log_area.code("\n".join(logs[-50:]), language="log")  # Show last 50 lines

                # Check result
                process.wait()
                if process.returncode == 0:
                    st.success("✅ Pipeline completed successfully!")
                else:
                    st.error(f"❌ Pipeline failed with code {process.returncode}")

    # ========================================================================
    # MODE 3: Manage Presets
    # ========================================================================
    elif mode == "Manage Presets":
        st.header("📚 Manage Presets")

        st.markdown("""
        Presets are predefined configurations you can quickly load and modify.

        **To add a new preset:**
        1. Edit `scripts/pipeline_gui.py`
        2. Find the `PRESETS` dictionary at the top
        3. Add your preset: `"preset_name": "configs/my_config.yaml"`
        """)

        st.divider()

        st.subheader("Available Presets")
        for name, path in PRESETS.items():
            col1, col2, col3 = st.columns([2, 4, 2])
            with col1:
                st.code(name)
            with col2:
                st.text(path)
            with col3:
                full_path = PROJECT_ROOT / path
                if full_path.exists():
                    st.success("✅ Exists")
                else:
                    st.error("❌ Missing")

        st.divider()

        st.subheader("📁 All Configs")
        config_files = list((PROJECT_ROOT / "configs").glob("*.yaml"))

        for config_file in sorted(config_files):
            with st.expander(f"📄 {config_file.name}"):
                with open(config_file, 'r') as f:
                    content = f.read()
                st.code(content, language="yaml")


if __name__ == "__main__":
    main()
