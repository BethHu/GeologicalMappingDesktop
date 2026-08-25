package com.example.geomappingmobile.ui.route

import android.view.LayoutInflater
import android.view.ViewGroup
import android.widget.TextView
import androidx.core.content.ContextCompat
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.example.geomappingmobile.R
import com.example.geomappingmobile.data.model.Route
import com.example.geomappingmobile.data.model.RouteStatus
import com.example.geomappingmobile.databinding.ItemRouteBinding
import com.example.geomappingmobile.util.formatDate
import com.example.geomappingmobile.util.formatDistance
import com.example.geomappingmobile.util.formatDuration
import com.example.geomappingmobile.util.toStatusColorRes

sealed class RouteListItem {
    data class Header(val title: String) : RouteListItem()
    data class RouteItem(val route: Route) : RouteListItem()
}

class RouteAdapter(private val onClick: (Route) -> Unit) :
    ListAdapter<RouteListItem, RecyclerView.ViewHolder>(DiffCallback()) {

    override fun getItemViewType(position: Int): Int {
        return if (getItem(position) is RouteListItem.Header) 0 else 1
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): RecyclerView.ViewHolder {
        return if (viewType == 0) {
            val tv = TextView(parent.context).apply {
                setTextColor(ContextCompat.getColor(context, R.color.text_secondary))
                textSize = 12f
                setPadding(4, 24, 4, 8)
                setTypeface(null, android.graphics.Typeface.BOLD)
            }
            HeaderViewHolder(tv)
        } else {
            RouteViewHolder(
                ItemRouteBinding.inflate(LayoutInflater.from(parent.context), parent, false),
                onClick
            )
        }
    }

    override fun onBindViewHolder(holder: RecyclerView.ViewHolder, position: Int) {
        val item = getItem(position)
        when {
            holder is HeaderViewHolder && item is RouteListItem.Header -> holder.bind(item)
            holder is RouteViewHolder && item is RouteListItem.RouteItem -> holder.bind(item)
        }
    }

    class HeaderViewHolder(itemView: TextView) : RecyclerView.ViewHolder(itemView) {
        fun bind(header: RouteListItem.Header) {
            (itemView as TextView).text = header.title
        }
    }

    class RouteViewHolder(
        private val binding: ItemRouteBinding,
        private val onClick: (Route) -> Unit
    ) : RecyclerView.ViewHolder(binding.root) {
        fun bind(item: RouteListItem.RouteItem) {
            val route = item.route
            binding.routeName.text = route.name.ifBlank { "未命名路线" }
            binding.routeDate.text = route.createdAt.formatDate()
            binding.routeDistance.text = String.format("距离: %s", route.totalDistance.formatDistance())
            binding.routeDuration.text = String.format("用时: %s", route.totalDuration.formatDuration())
            binding.routePointCount.text = String.format(" %d点", route.pointCount)

            val statusColor = ContextCompat.getColor(binding.root.context, route.status.toStatusColorRes())
            binding.statusBadge.apply {
                text = when (route.status) {
                    RouteStatus.RECORDING -> "记录中"
                    RouteStatus.PAUSED -> "已暂停"
                    RouteStatus.PLANNED -> "已规划"
                    RouteStatus.COMPLETED -> "已完成"
                }
                setTextColor(statusColor)
                background = ContextCompat.getDrawable(
                    binding.root.context,
                    if (route.status == RouteStatus.RECORDING) R.drawable.bg_badge
                    else android.R.color.transparent
                )
            }
            binding.root.setOnClickListener { onClick(route) }
        }
    }

    class DiffCallback : DiffUtil.ItemCallback<RouteListItem>() {
        override fun areItemsTheSame(oldItem: RouteListItem, newItem: RouteListItem): Boolean {
            return when {
                oldItem is RouteListItem.Header && newItem is RouteListItem.Header -> oldItem.title == newItem.title
                oldItem is RouteListItem.RouteItem && newItem is RouteListItem.RouteItem -> oldItem.route.id == newItem.route.id
                else -> false
            }
        }
        override fun areContentsTheSame(oldItem: RouteListItem, newItem: RouteListItem): Boolean = oldItem == newItem
    }
}