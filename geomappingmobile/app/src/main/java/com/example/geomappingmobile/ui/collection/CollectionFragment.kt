package com.example.geomappingmobile.ui.collection

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.core.content.ContextCompat
import androidx.fragment.app.Fragment
import androidx.recyclerview.widget.LinearLayoutManager
import com.example.geomappingmobile.R
import com.example.geomappingmobile.data.model.GeologicalPoint
import com.example.geomappingmobile.data.model.PointType
import com.example.geomappingmobile.data.repository.AppRepository
import com.example.geomappingmobile.databinding.FragmentCollectionBinding
import com.example.geomappingmobile.util.toColorRes

class CollectionFragment : Fragment() {

    private var _binding: FragmentCollectionBinding? = null
    private val binding get() = _binding!!

    private lateinit var adapter: PointAdapter
    private val selectedTypes = mutableSetOf<PointType>()
    private var searchQuery: String = ""
    private var sortMode: SortMode = SortMode.TIME_NEWEST

    enum class SortMode { TIME_NEWEST, TIME_OLDEST, BY_TYPE }

    private val typeChips get() = listOf(
        binding.chipGeological to PointType.GEOLOGICAL,
        binding.chipStructural to PointType.STRUCTURAL,
        binding.chipSample to PointType.SAMPLE,
        binding.chipPhoto to PointType.PHOTO,
        binding.chipLandform to PointType.LANDFORM,
        binding.chipHydrological to PointType.HYDROLOGICAL,
        binding.chipEngineering to PointType.ENGINEERING
    )

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentCollectionBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        setupRecyclerView()
        setupChips()
        setupSearch()
        setupSort()
        setupClickListeners()
        observePoints()
    }

    private fun setupSearch() {
        binding.searchEditText.addTextChangedListener(object : android.text.TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            override fun afterTextChanged(s: android.text.Editable?) {
                searchQuery = s?.toString() ?: ""
                binding.btnClearSearch.visibility = if (searchQuery.isNotEmpty()) View.VISIBLE else View.GONE
                refreshPoints()
            }
        })
        binding.btnClearSearch.setOnClickListener {
            binding.searchEditText.setText("")
        }
    }

    private fun setupSort() {
        binding.sortTimeNewest.setOnClickListener { selectSort(SortMode.TIME_NEWEST) }
        binding.sortTimeOldest.setOnClickListener { selectSort(SortMode.TIME_OLDEST) }
        binding.sortByType.setOnClickListener { selectSort(SortMode.BY_TYPE) }
    }

    private fun selectSort(mode: SortMode) {
        sortMode = mode
        binding.sortTimeNewest.isChecked = mode == SortMode.TIME_NEWEST
        binding.sortTimeOldest.isChecked = mode == SortMode.TIME_OLDEST
        binding.sortByType.isChecked = mode == SortMode.BY_TYPE
        refreshPoints()
    }

    private fun setupRecyclerView() {
        adapter = PointAdapter { point -> onPointClicked(point) }
        binding.pointRecyclerView.layoutManager = LinearLayoutManager(requireContext())
        binding.pointRecyclerView.adapter = adapter
    }

    private fun createColorDot(color: Int): android.graphics.drawable.GradientDrawable {
        return android.graphics.drawable.GradientDrawable().apply {
            shape = android.graphics.drawable.GradientDrawable.OVAL
            setSize(48, 48)
            setColor(color)
        }
    }

    private fun setupChips() {
        selectedTypes.clear()
        updateFilterChips()

        binding.chipAll.setOnClickListener {
            selectedTypes.clear()
            updateFilterChips()
            AppRepository.setPointTypeFilter(null)
            refreshPoints()
        }

        typeChips.forEach { (btn, type) ->
            btn.setOnClickListener {
                if (selectedTypes.size == 1 && selectedTypes.contains(type)) {
                    selectedTypes.clear()
                } else {
                    selectedTypes.clear()
                    selectedTypes.add(type)
                }
                updateFilterChips()
                AppRepository.setPointTypeFilter(if (selectedTypes.size == 1) selectedTypes.first() else null)
                refreshPoints()
            }
        }
    }

    private fun updateFilterChips() {
        val showAll = selectedTypes.isEmpty()
        binding.chipAll.isChecked = showAll
        binding.chipAll.setTextColor(ContextCompat.getColor(requireContext(), if (showAll) R.color.primary else R.color.text_secondary))
        typeChips.forEach { (btn, type) ->
            val selected = type in selectedTypes
            btn.isChecked = selected
            btn.setTextColor(ContextCompat.getColor(requireContext(), if (selected) R.color.primary else R.color.text_secondary))
        }
    }

    private fun setupClickListeners() {
        binding.btnNewPoint.setOnClickListener { showNewPointDialog() }
    }

    private fun showNewPointDialog() {
        val ctx = requireContext()
        val textColor = ContextCompat.getColor(ctx, R.color.text_primary)
        val hintColor = ContextCompat.getColor(ctx, R.color.text_disabled)

        val listView = android.widget.ListView(ctx).apply {
            divider = null
            dividerHeight = 0
            setPadding(0, 8, 0, 8)
        }

        val entries = PointType.entries
        val adapter = object : android.widget.BaseAdapter() {
            override fun getCount() = entries.size
            override fun getItem(pos: Int) = entries[pos]
            override fun getItemId(pos: Int) = pos.toLong()

            override fun getView(pos: Int, convertView: View?, parent: ViewGroup): View {
                val type = entries[pos]
                val color = ContextCompat.getColor(ctx, type.toColorRes())
                val row = android.widget.LinearLayout(ctx).apply {
                    orientation = android.widget.LinearLayout.HORIZONTAL
                    gravity = android.view.Gravity.CENTER_VERTICAL
                    setPadding(24, 12, 24, 12)
                    minimumHeight = 56
                    isClickable = true
                    setOnClickListener {
                        openPointDetail(GeologicalPoint(pointType = type))
                        (parent as? android.app.AlertDialog)?.dismiss()
                    }
                }
                val dot = android.widget.ImageView(ctx).apply {
                    layoutParams = android.widget.LinearLayout.LayoutParams(14, 14).apply {
                        setMargins(0, 0, 14, 0)
                    }
                    setImageDrawable(createColorDot(color))
                }
                row.addView(dot)
                val textCol = android.widget.LinearLayout(ctx).apply {
                    orientation = android.widget.LinearLayout.VERTICAL
                }
                textCol.addView(android.widget.TextView(ctx).apply {
                    text = type.displayName
                    setTextColor(textColor)
                    textSize = 15f
                    typeface = android.graphics.Typeface.DEFAULT_BOLD
                })
                textCol.addView(android.widget.TextView(ctx).apply {
                    text = type.description
                    setTextColor(hintColor)
                    textSize = 12f
                })
                row.addView(textCol)
                return row
            }
        }
        listView.adapter = adapter

        android.app.AlertDialog.Builder(ctx)
            .setTitle(R.string.collection_select_type)
            .setView(listView)
            .setNegativeButton("取消", null)
            .show()
    }

    private fun observePoints() {
        AppRepository.points.observe(viewLifecycleOwner) { refreshPoints() }
    }

    private fun refreshPoints() {
        val allPoints = AppRepository.points.value ?: emptyList()
        var result = if (selectedTypes.isEmpty()) allPoints
        else allPoints.filter { it.pointType in selectedTypes }

        if (searchQuery.isNotBlank()) {
            val q = searchQuery.trim().lowercase()
            result = result.filter { p ->
                p.description.lowercase().contains(q) ||
                p.customId.lowercase().contains(q) ||
                p.id.lowercase().contains(q)
            }
        }

        result = when (sortMode) {
            SortMode.TIME_NEWEST -> result.sortedByDescending { it.timestamp }
            SortMode.TIME_OLDEST -> result.sortedBy { it.timestamp }
            SortMode.BY_TYPE -> result.sortedBy { it.pointType }
        }

        adapter.submitList(result)
        binding.emptyView.visibility = if (result.isEmpty()) View.VISIBLE else View.GONE
    }

    private fun onPointClicked(point: GeologicalPoint) {
        openPointDetail(point)
    }

    private fun openPointDetail(point: GeologicalPoint) {
        val dialog = PointDetailDialog.newInstance(point)
        dialog.show(childFragmentManager, "PointDetail")
        dialog.setOnSaveListener { savedPoint ->
            if (point.id in (AppRepository.points.value?.map { it.id } ?: emptyList())) {
                AppRepository.updatePoint(savedPoint)
            } else {
                AppRepository.addPoint(savedPoint)
            }
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
