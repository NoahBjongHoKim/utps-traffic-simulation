# Streamlit GUI for Pipeline Configuration

A user-friendly interface for creating, managing, and running traffic simulation pipeline configurations.

## Installation

Install Streamlit:
```bash
pip install streamlit
```

## Running the GUI

```bash
cd /Users/noahkim/Documents/UTPS/Traffic_Sim/utps-ts-repo
streamlit run scripts/pipeline_gui.py
```

The GUI will open in your browser at `http://localhost:8501`

## Features

### 1. Create/Edit Config Mode
- Build configurations using forms (no YAML editing!)
- Load existing presets and modify them
- Real-time validation
- Preview YAML before saving
- Calculate number of snapshots automatically

### 2. Run Pipeline Mode
- Select and run any saved configuration
- View live logs as pipeline runs
- See success/failure status

### 3. Manage Presets Mode
- View all available presets
- Check which config files exist
- Browse all configurations

## Customization Guide

### Adding New Presets

Edit `scripts/pipeline_gui.py` and find this section (around line 30):

```python
# Available presets (add your own here!)
PRESETS = {
    "v4_test": "configs/v4_default.yaml",
    "v4_snapshots": "configs/v4_snapshots.yaml",
    # Add your presets here:
    "my_preset": "configs/my_config.yaml",
}
```

### Changing Default Values

Find the `DEFAULTS` dictionary (around line 36):

```python
DEFAULTS = {
    "xml_input": "data/raw/output_events.xml.gz",
    "gpkg_network": "data/raw/road_network_v4_clipped.gpkg",
    "start_time": "12:00",  # Change defaults here
    "end_time": "15:00",
    "frequency_seconds": 300,  # 5 minutes
    "duration_seconds": 5,
    # ...
}
```

### Customizing Output Formats

Edit the `FORMAT_HELP` dictionary to add tooltips for new formats:

```python
FORMAT_HELP = {
    "geojson": "Standard format for web visualization",
    "csv": "Simple format for Excel/spreadsheets",
    "parquet": "Columnar format for data analysis",
    "geoparquet": "Spatial parquet - best for ArcGIS!",
    # Add your format descriptions here
}
```

### Adding New Configuration Fields

To add new fields to the GUI:

1. **Add to the form** (around line 150-250):
   ```python
   # Example: Add a new field
   my_new_option = st.text_input(
       "My New Option",
       value=config_dict.get("my_section", {}).get("my_field", "default"),
       help="Description of what this does"
   )
   ```

2. **Add to config dict** (around line 290):
   ```python
   new_config = {
       "paths": { ... },
       "filters": { ... },
       "processing": { ... },
       "my_section": {  # Add your section here
           "my_field": my_new_option
       }
   }
   ```

### Styling and Layout

Streamlit uses a simple API. Key components:

```python
# Headers
st.header("My Header")
st.subheader("My Subheader")

# Columns for layout
col1, col2 = st.columns(2)
with col1:
    st.write("Left column")
with col2:
    st.write("Right column")

# Input widgets
text = st.text_input("Label", value="default")
number = st.number_input("Label", min_value=1, max_value=100)
checkbox = st.checkbox("Label", value=True)
dropdown = st.selectbox("Label", ["Option 1", "Option 2"])

# Messages
st.success("✅ Success message")
st.error("❌ Error message")
st.warning("⚠️ Warning message")
st.info("ℹ️ Info message")

# Expandable sections
with st.expander("Click to expand"):
    st.write("Hidden content")

# Code display
st.code("print('hello')", language="python")
```

## Advanced Customization

### Adding Pipeline Steps

To add new pipeline steps or options:

1. Update the Pydantic model in `main_pipeline.py`
2. Add the field to the GUI form
3. Include it in the `new_config` dictionary

### Custom Validation

Add custom validation in the GUI:

```python
# Example: Validate file exists
if not Path(xml_input).exists():
    st.error(f"File not found: {xml_input}")
```

### Progress Indicators

For long-running operations:

```python
import time

progress_bar = st.progress(0)
for i in range(100):
    time.sleep(0.1)
    progress_bar.progress(i + 1)
```

## Tips

1. **Auto-reload**: Streamlit automatically reloads when you edit `pipeline_gui.py`
2. **Debug mode**: Add `st.write(variable)` anywhere to inspect values
3. **Session state**: Use `st.session_state` to persist data between reruns
4. **Caching**: Use `@st.cache_data` decorator for expensive operations

## Troubleshooting

**Port already in use:**
```bash
streamlit run scripts/pipeline_gui.py --server.port 8502
```

**Can't find modules:**
- Make sure you're running from the project root
- Check that the conda environment is activated

**Pipeline not running:**
- Check logs in the Run Pipeline mode
- Verify config file paths are correct
- Test the config with CLI first: `python -m traffic_sim_module.pipelines.main_pipeline configs/test.yaml`

## Example Workflow

1. **Start GUI**: `streamlit run scripts/pipeline_gui.py`
2. **Create Config**:
   - Go to "Create/Edit Config" mode
   - Load "v4_snapshots" preset
   - Modify parameters as needed
   - Save as "my_experiment.yaml"
3. **Run Pipeline**:
   - Switch to "Run Pipeline" mode
   - Select "my_experiment.yaml"
   - Click "Run Pipeline"
   - Monitor logs in real-time
4. **Iterate**:
   - Go back to Create/Edit
   - Load "my_experiment.yaml"
   - Tweak parameters
   - Save and run again

## Resources

- [Streamlit Documentation](https://docs.streamlit.io/)
- [Streamlit Cheat Sheet](https://cheat-sheet.streamlit.app/)
- [Streamlit Gallery](https://streamlit.io/gallery) - Examples and inspiration
