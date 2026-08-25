package com.example.geomappingmobile.ui.collection

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.core.content.ContextCompat
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.example.geomappingmobile.R
import com.example.geomappingmobile.data.model.GeologicalPoint
import com.example.geomappingmobile.databinding.ItemPointBinding
import com.example.geomappingmobile.util.formatCoord
import com.example.geomappingmobile.util.formatDateTime
import com.example.geomappingmobile.util.toColorRes

class PointAdapter(private val onClick: (GeologicalPoint) -> Unit) :
    ListAdapter<GeologicalPoint, PointAdapter.ViewHolder>(DiffCallback()) {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemPointBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return ViewHolder(binding, onClick)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    class ViewHolder(
        private val binding: ItemPointBinding,
        private val onClick: (GeologicalPoint) -> Unit
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(point: GeologicalPoint) {
            val context = binding.root.context

            binding.pointType.text = point.pointType.displayName
            binding.pointId.text = "#${point.customId.ifBlank { point.id }}"

            val desc = point.description.ifBlank { "暂无描述" }
            binding.pointDescription.text = desc

            binding.pointCoord.text = String.format(
                "%.5f, %.5f", point.latitude, point.longitude
            )
            binding.pointTime.text = point.timestamp.formatDateTime()

            val color = ContextCompat.getColor(context, point.pointType.toColorRes())
            binding.typeIndicator.setBackgroundColor(color)

            binding.root.setOnClickListener { onClick(point) }
        }
    }

    class DiffCallback : DiffUtil.ItemCallback<GeologicalPoint>() {
        override fun areItemsTheSame(oldItem: GeologicalPoint, newItem: GeologicalPoint) = oldItem.id == newItem.id
        override fun areContentsTheSame(oldItem: GeologicalPoint, newItem: GeologicalPoint) = oldItem == newItem
    }
}
