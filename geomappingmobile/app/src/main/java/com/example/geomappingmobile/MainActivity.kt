package com.example.geomappingmobile

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.view.ViewCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import com.example.geomappingmobile.databinding.ActivityMainBinding
import com.example.geomappingmobile.ui.collection.CollectionFragment
import com.example.geomappingmobile.ui.map.MapFragment
import com.example.geomappingmobile.ui.route.RouteFragment
import com.example.geomappingmobile.ui.settings.SettingsFragment
import com.google.android.material.bottomnavigation.BottomNavigationView

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding

    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        android.util.Log.d("MainActivity", "onCreate start")
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        WindowCompat.setDecorFitsSystemWindows(window, false)

        requestPermissions()
        setupNavigation()
        applyWindowInsets()
        android.util.Log.d("MainActivity", "onCreate done")
    }

    private fun applyWindowInsets() {
        val bottomNav = findViewById<BottomNavigationView>(R.id.bottom_nav)
        ViewCompat.setOnApplyWindowInsetsListener(binding.root) { v, insets ->
            val navBarInsets = insets.getInsets(WindowInsetsCompat.Type.navigationBars())
            val statusBarInsets = insets.getInsets(WindowInsetsCompat.Type.statusBars())

            bottomNav.setPadding(
                bottomNav.paddingLeft,
                bottomNav.paddingTop,
                bottomNav.paddingRight,
                navBarInsets.bottom
            )

            binding.fragmentContainer.setPadding(
                binding.fragmentContainer.paddingLeft,
                statusBarInsets.top,
                binding.fragmentContainer.paddingRight,
                binding.fragmentContainer.paddingBottom
            )

            insets
        }
    }

    private val fragmentTags = mapOf(
        R.id.mapFragment to "MapFragment",
        R.id.routeFragment to "RouteFragment",
        R.id.collectionFragment to "CollectionFragment",
        R.id.settingsFragment to "SettingsFragment"
    )

    private fun setupNavigation() {
        val bottomNav = findViewById<BottomNavigationView>(R.id.bottom_nav)
        switchFragment(R.id.mapFragment)
        bottomNav.setOnItemSelectedListener { item ->
            switchFragment(item.itemId)
            true
        }
    }

    private fun switchFragment(id: Int) {
        val tag = fragmentTags[id] ?: return
        val existing = supportFragmentManager.findFragmentByTag(tag)
        if (existing != null && existing.isAdded) return

        val fragment = when (id) {
            R.id.mapFragment -> MapFragment()
            R.id.routeFragment -> RouteFragment()
            R.id.collectionFragment -> CollectionFragment()
            R.id.settingsFragment -> SettingsFragment()
            else -> MapFragment()
        }
        supportFragmentManager.beginTransaction()
            .replace(R.id.fragment_container, fragment, tag)
            .commitNow()
    }

    private fun requestPermissions() {
        val permissions = mutableListOf(
            Manifest.permission.ACCESS_FINE_LOCATION,
            Manifest.permission.ACCESS_COARSE_LOCATION
        )
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            permissions.add(Manifest.permission.ACCESS_BACKGROUND_LOCATION)
        }
        if (Build.VERSION.SDK_INT <= Build.VERSION_CODES.P) {
            permissions.add(Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }
        permissions.add(Manifest.permission.CAMERA)

        val needed = permissions.filter {
            ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED
        }
        if (needed.isNotEmpty()) {
            permissionLauncher.launch(needed.toTypedArray())
        }
    }
}
